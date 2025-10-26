using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

// [TODO] 批量 A*（一条请求 = 一次 A*），并行执行；每条结果写入 NativeStream 的独立 forEachIndex 槽
[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
public struct GridAStarBatchJob : IJobParallelFor
{
    // 网格
    [ReadOnly] public int Width;
    [ReadOnly] public int Height;
    [ReadOnly] public NativeArray<byte> Buildable;     // grid.passableType 拷贝/共享
    [ReadOnly] public NativeArray<byte> PassMask256;   // 允许通行集合（byte 值映射到 0/1）

    // 策略
    [ReadOnly] public byte AllowDiagonal;  // 0/1
    [ReadOnly] public byte BlockCornerCut; // 0/1（禁止斜穿角）

    // 批量请求
    [ReadOnly] public NativeArray<int> Starts; // 每条：起点索引
    [ReadOnly] public NativeArray<int> Goals;  // 每条：终点索引

    // 输出（每个 forEachIndex 对应一条路径的数据流）
    public NativeStream.Writer Writer;

    // --- 内部常量 ---
    private const int COST_ORTHO = 10;
    private const int COST_DIAG = 14;

    public void Execute(int index)
    {
        int start = Starts[index];
        int goal = Goals[index];
        int N = Width * Height;

        Writer.BeginForEachIndex(index);

        // 校验
        if (!InBoundsIndex(start, N) || !InBoundsIndex(goal, N) ||
            PassMask256[Buildable[start]] == 0 || PassMask256[Buildable[goal]] == 0)
        {
            Writer.Write(0); // 写入长度 0 表示不可达
            Writer.EndForEachIndex();
            return;
        }

        // 申请临时数组（单请求局部，Temp 分配；可视项目规模适度调度并行度）
        var g = new NativeArray<int>(N, Allocator.Temp);
        var f = new NativeArray<int>(N, Allocator.Temp);
        var parent = new NativeArray<int>(N, Allocator.Temp);
        var closed = new NativeArray<byte>(N, Allocator.Temp);
        var heap = new NativeArray<int>(N, Allocator.Temp);
        var pos = new NativeArray<int>(N, Allocator.Temp);
        int heapSize = 0;

        for (int i = 0; i < N; i++)
        {
            g[i] = int.MaxValue; f[i] = int.MaxValue; parent[i] = -1; closed[i] = 0; pos[i] = -1;
        }

        int sx = start % Width, sz = start / Width;
        int gx = goal % Width, gz = goal / Width;

        g[start] = 0;
        f[start] = Heuristic(sx, sz, gx, gz);
        HeapPush(start, f, heap, pos, ref heapSize);

        bool found = false;

        while (heapSize > 0)
        {
            int cur = HeapPop(f, heap, pos, ref heapSize);
            if (cur == goal) { found = true; break; }
            closed[cur] = 1;

            int cx = cur % Width, cz = cur / Width;

            if (AllowDiagonal != 0)
            {
                // 8 邻域
                for (int dir = 0; dir < 8; dir++)
                {
                    int nx, nz, stepCost; Neighbor8(cx, cz, dir, out nx, out nz, out stepCost);
                    if (!InBounds(nx, nz)) continue;
                    int ni = nx + Width * nz;
                    if (closed[ni] != 0 || PassMask256[Buildable[ni]] == 0) continue;

                    if (BlockCornerCut != 0 && dir >= 4)
                    {
                        int ox = (dir == 4 || dir == 5) ? cx + 1 : cx - 1;
                        int oz = (dir == 4 || dir == 6) ? cz + 1 : cz - 1;
                        if (!InBounds(ox, cz) || !InBounds(cx, oz)) continue;
                        int i1 = ox + Width * cz, i2 = cx + Width * oz;
                        if (PassMask256[Buildable[i1]] == 0 || PassMask256[Buildable[i2]] == 0) continue;
                    }

                    int newG = g[cur] + stepCost;
                    if (newG >= g[ni]) continue;

                    g[ni] = newG;
                    f[ni] = newG + Heuristic(nx, nz, gx, gz);
                    parent[ni] = cur;

                    int p = pos[ni];
                    if (p >= 0) HeapDecreaseKey(p, f, heap, pos);
                    else HeapPush(ni, f, heap, pos, ref heapSize);
                }
            }
            else
            {
                // 4 邻域
                for (int dir = 0; dir < 4; dir++)
                {
                    int nx, nz; Neighbor4(cx, cz, dir, out nx, out nz);
                    if (!InBounds(nx, nz)) continue;
                    int ni = nx + Width * nz;
                    if (closed[ni] != 0 || PassMask256[Buildable[ni]] == 0) continue;

                    int newG = g[cur] + COST_ORTHO;
                    if (newG >= g[ni]) continue;

                    g[ni] = newG;
                    f[ni] = newG + Heuristic(nx, nz, gx, gz);
                    parent[ni] = cur;

                    int p = pos[ni];
                    if (p >= 0) HeapDecreaseKey(p, f, heap, pos);
                    else HeapPush(ni, f, heap, pos, ref heapSize);
                }
            }
        }

        if (!found)
        {
            Writer.Write(0); // 不可达
        }
        else
        {
            // 回溯并写入：先写长度，再写索引序列（正序）
            int len = 0;
            int cursor = goal;
            while (cursor >= 0) { len++; if (cursor == start) break; cursor = parent[cursor]; }

            if (cursor != start) { Writer.Write(0); } // 保险
            else
            {
                Writer.Write(len);
                var tmp = new NativeArray<int>(len, Allocator.Temp);
                cursor = goal;
                for (int i = len - 1; i >= 0; i--)
                {
                    tmp[i] = cursor;
                    cursor = parent[cursor];
                }
                for (int i = 0; i < len; i++) Writer.Write(tmp[i]);
                tmp.Dispose();
            }
        }

        // 释放
        g.Dispose(); f.Dispose(); parent.Dispose(); closed.Dispose(); heap.Dispose(); pos.Dispose();

        Writer.EndForEachIndex();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool InBoundsIndex(int idx, int N) => (uint)idx < (uint)N;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool InBounds(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Height;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Heuristic(int x0, int z0, int x1, int z1)
    {
        int dx = math.abs(x0 - x1);
        int dz = math.abs(z0 - z1);
        if (AllowDiagonal == 0) return (dx + dz) * COST_ORTHO;
        int m = math.min(dx, dz), M = math.max(dx, dz);
        return m * COST_DIAG + (M - m) * COST_ORTHO;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Neighbor4(int x, int z, int dir, out int nx, out int nz)
    {
        if (dir == 0) { nx = x + 1; nz = z; return; }
        if (dir == 1) { nx = x - 1; nz = z; return; }
        if (dir == 2) { nx = x; nz = z + 1; return; }
        nx = x; nz = z - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Neighbor8(int x, int z, int dir, out int nx, out int nz, out int stepCost)
    {
        if (dir == 0) { nx = x + 1; nz = z; stepCost = COST_ORTHO; return; }
        if (dir == 1) { nx = x - 1; nz = z; stepCost = COST_ORTHO; return; }
        if (dir == 2) { nx = x; nz = z + 1; stepCost = COST_ORTHO; return; }
        if (dir == 3) { nx = x; nz = z - 1; stepCost = COST_ORTHO; return; }
        if (dir == 4) { nx = x + 1; nz = z + 1; stepCost = COST_DIAG; return; }
        if (dir == 5) { nx = x + 1; nz = z - 1; stepCost = COST_DIAG; return; }
        if (dir == 6) { nx = x - 1; nz = z + 1; stepCost = COST_DIAG; return; }
        nx = x - 1; nz = z - 1; stepCost = COST_DIAG;
    }

    // --- 最小堆：按 f 值 ---
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HeapPush(int idx, NativeArray<int> f, NativeArray<int> heap, NativeArray<int> pos, ref int size)
    {
        int i = size++; heap[i] = idx; pos[idx] = i; SiftUp(i, f, heap, pos);
    }

    private static int HeapPop(NativeArray<int> f, NativeArray<int> heap, NativeArray<int> pos, ref int size)
    {
        int root = heap[0];
        int last = heap[--size];
        heap[0] = last; pos[last] = 0; pos[root] = -1;
        if (size > 0) SiftDown(0, size, f, heap, pos);
        return root;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void HeapDecreaseKey(int i, NativeArray<int> f, NativeArray<int> heap, NativeArray<int> pos)
    {
        SiftUp(i, f, heap, pos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SiftUp(int i, NativeArray<int> f, NativeArray<int> heap, NativeArray<int> pos)
    {
        int idx = heap[i]; int fi = f[idx];
        while (i > 0)
        {
            int p = (i - 1) >> 1; int pIdx = heap[p];
            if (fi >= f[pIdx]) break;
            heap[i] = pIdx; pos[pIdx] = i; i = p;
        }
        heap[i] = idx; pos[idx] = i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SiftDown(int i, int size, NativeArray<int> f, NativeArray<int> heap, NativeArray<int> pos)
    {
        int idx = heap[i]; int fi = f[idx];
        int half = size >> 1;
        while (i < half)
        {
            int l = (i << 1) + 1; int r = l + 1;
            int best = l; int bIdx = heap[best]; int bf = f[bIdx];
            if (r < size)
            {
                int rIdx = heap[r]; int rf = f[rIdx];
                if (rf < bf) { best = r; bIdx = rIdx; bf = rf; }
            }
            if (fi <= bf) break;
            heap[i] = bIdx; pos[bIdx] = i; i = best;
        }
        heap[i] = idx; pos[idx] = i;
    }
}
