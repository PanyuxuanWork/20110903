#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class TaskServiceDebugWindow : EditorWindow
{
    private const int MaxLogCount = 200;
    private const double PollInterval = 0.20d;

    private Vector2 _queueScroll;
    private Vector2 _taskScroll;
    private Vector2 _detailScroll;
    private Vector2 _historyScroll;
    private Vector2 _logScroll;

    private TaskBase _selectedTask;
    private TaskQueue _selectedQueue;
    private double _lastPollTime;

    private readonly List<TraceEntry> _logs = new();
    private readonly Dictionary<int, TaskTrack> _tracks = new();
    private readonly List<TaskQueue> _queues = new();

    [MenuItem("Tools/Task Service/Debug Window")]
    private static void Open()
    {
        var window = GetWindow<TaskServiceDebugWindow>("TaskService Debug");
        window.minSize = new Vector2(1200f, 700f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        if (!Application.isPlaying)
        {
            if (_queues.Count > 0 || _logs.Count > 0 || _selectedTask != null || _selectedQueue != null)
            {
                _queues.Clear();
                _tracks.Clear();
                _logs.Clear();
                _selectedTask = null;
                _selectedQueue = null;
                Repaint();
            }
            return;
        }

        if (EditorApplication.timeSinceStartup - _lastPollTime < PollInterval)
            return;

        _lastPollTime = EditorApplication.timeSinceStartup;
        RebuildViewData();
        Repaint();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("进入 Play Mode 后显示运行时状态。", MessageType.Info);
            return;
        }

        var service = TaskService.Instance;
        if (service == null)
        {
            EditorGUILayout.HelpBox("未找到 TaskService.Instance。", MessageType.Warning);
            return;
        }

        DrawServiceHeader(service);

        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawQueuePanel(service);
            DrawTaskPanel();
            DrawDetailPanel();
        }

        EditorGUILayout.Space(6);

        using (new EditorGUILayout.HorizontalScope())
        {
            DrawHistoryPanel();
            DrawLogPanel();
        }
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("TaskService Runtime Debugger", EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RebuildViewData();
                Repaint();
            }

            if (GUILayout.Button("Clear Logs", EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                _logs.Clear();
                Repaint();
            }
        }
    }

    private void DrawServiceHeader(TaskService service)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Service", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMiniStat("State", service.ServiceState.ToString());
                DrawMiniStat("Queue Count", service.QueueCount.ToString());
                DrawMiniStat("Is Active", service.IsActive ? "True" : "False");
                DrawMiniStat("Global Tick", GlobalStep.Instance != null ? "True" : "False");
                if (GlobalStep.Instance != null)
                    DrawMiniStat("Tick Seconds", GlobalStep.Instance.tickSeconds.ToString("F3"));
                DrawMiniStat("Time Scale", Time.timeScale.ToString("F2"));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = service.ServiceState != TaskServiceState.Paused && service.ServiceState != TaskServiceState.Stopped;
                if (GUILayout.Button("Pause")) service.Pause();

                GUI.enabled = service.ServiceState == TaskServiceState.Paused;
                if (GUILayout.Button("Resume")) service.Resume();

                GUI.enabled = true;
                if (GUILayout.Button("Cancel Current")) service.CancelAllCurrent("Canceled from debug window", TaskCancelReason.Manual);
                if (GUILayout.Button("Clear Pending")) service.ClearAllPending();
                if (GUILayout.Button("Stop Service")) service.Stop(cancelCurrent: true, clearPending: true);
                GUI.enabled = true;
            }
        }
    }

    private void DrawQueuePanel(TaskService service)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(position.width * 0.23f), GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Queues", EditorStyles.boldLabel);
            _queueScroll = EditorGUILayout.BeginScrollView(_queueScroll);

            if (_queues.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有 Queue。这个窗口不会自动发现独立创建的 TaskQueue，只有调用 TaskService.AddQueue(queue) 后，GetQueuesSnapshot() 才能看到它。", MessageType.Info);
            }
            else
            {
                foreach (var queue in _queues)
                {
                    bool selected = ReferenceEquals(_selectedQueue, queue);

                    var style = new GUIStyle(EditorStyles.helpBox);
                    if (selected)
                        style.normal.background = Texture2D.grayTexture;

                    using (new EditorGUILayout.VerticalScope(style))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Toggle(selected, "", GUILayout.Width(18f)) != selected)
                            {
                                _selectedQueue = queue;
                                if (queue.Current != null)
                                    _selectedTask = queue.Current;
                            }

                            EditorGUILayout.LabelField(queue.QueueName, EditorStyles.boldLabel);
                        }

                        EditorGUILayout.LabelField("State", queue.State.ToString());
                        EditorGUILayout.LabelField("IsActive", queue.IsActive ? "True" : "False");
                        EditorGUILayout.LabelField("HasTasks", queue.HasTasks ? "True" : "False");
                        EditorGUILayout.LabelField("PendingCount", queue.PendingCount.ToString());
                        EditorGUILayout.LabelField("HistoryCount", queue.HistoryCount.ToString());
                        EditorGUILayout.LabelField("FailurePolicy", queue.FailurePolicy.ToString());

                        if (GUILayout.Button("Select Queue"))
                        {
                            _selectedQueue = queue;
                            if (queue.Current != null)
                                _selectedTask = queue.Current;
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawTaskPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(position.width * 0.39f), GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Tasks", EditorStyles.boldLabel);
            _taskScroll = EditorGUILayout.BeginScrollView(_taskScroll);

            if (_selectedQueue == null)
            {
                EditorGUILayout.HelpBox("先在左侧选一个 Queue。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField(_selectedQueue.QueueName, EditorStyles.boldLabel);
                EditorGUILayout.Space(4);

                EditorGUILayout.LabelField("Current", EditorStyles.miniBoldLabel);
                if (_selectedQueue.Current != null)
                    DrawTaskCard(_selectedQueue.Current, true);
                else
                    EditorGUILayout.LabelField("-");

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Pending", EditorStyles.miniBoldLabel);
                var pending = _selectedQueue.GetPendingSnapshot();
                bool anyPending = false;
                foreach (var task in pending)
                {
                    anyPending = true;
                    DrawTaskCard(task, false);
                }
                if (!anyPending)
                    EditorGUILayout.LabelField("-");

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("History (latest first)", EditorStyles.miniBoldLabel);
                var history = new List<TaskQueueExecutionRecord>(_selectedQueue.GetHistorySnapshot());
                history.Reverse();
                foreach (var h in history)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(h.TaskName ?? "-", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("Outcome", h.Outcome.ToString());
                        EditorGUILayout.LabelField("FinalState", h.FinalState.ToString());
                        if (!string.IsNullOrEmpty(h.Message))
                            EditorGUILayout.LabelField("Message", h.Message);
                        if (h.FailReason != TaskFailReason.None)
                            EditorGUILayout.LabelField("FailReason", h.FailReason.ToString());
                        if (h.CancelReason != TaskCancelReason.None)
                            EditorGUILayout.LabelField("CancelReason", h.CancelReason.ToString());
                        if (!string.IsNullOrEmpty(h.NodeId))
                            EditorGUILayout.LabelField("NodeId", h.NodeId);
                        if (!string.IsNullOrEmpty(h.DependencyKey))
                            EditorGUILayout.LabelField("DependencyKey", h.DependencyKey);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawDetailPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(position.width * 0.34f), GUILayout.ExpandHeight(true)))
        {
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            if (_selectedTask == null)
            {
                EditorGUILayout.HelpBox("在中间选中一个 Task 后，这里会显示状态与原因。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("Task", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Name", string.IsNullOrEmpty(_selectedTask.Name) ? _selectedTask.GetType().Name : _selectedTask.Name);
                EditorGUILayout.LabelField("Type", _selectedTask.GetType().Name);
                EditorGUILayout.LabelField("State", _selectedTask.State.ToString());
                EditorGUILayout.LabelField("NodeId", string.IsNullOrEmpty(_selectedTask.NodeId) ? "-" : _selectedTask.NodeId);
                EditorGUILayout.LabelField("DependencyKey", string.IsNullOrEmpty(_selectedTask.DependencyKey) ? "-" : _selectedTask.DependencyKey);

                var result = _selectedTask.Result;
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

                if (result == null)
                {
                    EditorGUILayout.LabelField("No result yet.");
                }
                else
                {
                    EditorGUILayout.LabelField("Outcome", result.Outcome.ToString());
                    EditorGUILayout.LabelField("Duration", result.Duration.ToString("F3"));
                    EditorGUILayout.LabelField("Message", string.IsNullOrEmpty(result.Message) ? "-" : result.Message);
                    EditorGUILayout.LabelField("ErrorCode", string.IsNullOrEmpty(result.ErrorCode) ? "-" : result.ErrorCode);
                    EditorGUILayout.LabelField("IsFatal", result.IsFatal ? "True" : "False");
                    EditorGUILayout.LabelField("FailReason", result.FailReason.ToString());
                    EditorGUILayout.LabelField("CancelReason", result.CancelReason.ToString());

                    if (result.Exception != null)
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Exception", EditorStyles.boldLabel);
                        EditorGUILayout.TextArea(result.Exception.ToString(), GUILayout.MinHeight(80f));
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawHistoryPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.Height(190f)))
        {
            EditorGUILayout.LabelField("History", EditorStyles.boldLabel);
            _historyScroll = EditorGUILayout.BeginScrollView(_historyScroll);

            if (_selectedQueue == null)
            {
                EditorGUILayout.LabelField("选中一个 Queue 后显示历史。");
            }
            else
            {
                var history = new List<TaskQueueExecutionRecord>(_selectedQueue.GetHistorySnapshot());
                history.Reverse();

                if (history.Count == 0)
                {
                    EditorGUILayout.LabelField("No history yet.");
                }
                else
                {
                    foreach (var h in history)
                    {
                        EditorGUILayout.LabelField(
                            $"{h.TaskName} | {h.Outcome} | {h.FinalState} | {h.Message}",
                            EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawLogPanel()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.Height(190f)))
        {
            EditorGUILayout.LabelField("Live Transitions", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll);

            if (_logs.Count == 0)
            {
                EditorGUILayout.LabelField("No logs yet.");
            }
            else
            {
                for (int i = _logs.Count - 1; i >= 0; i--)
                {
                    var entry = _logs[i];
                    EditorGUILayout.LabelField($"[{entry.Time:HH:mm:ss.fff}] {entry.Text}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawTaskCard(TaskBase task, bool isCurrent)
    {
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = GetTaskColor(task.State);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUI.backgroundColor = old;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(isCurrent ? "●" : "○", GUILayout.Width(14f));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(task.Name) ? task.GetType().Name : task.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Select", GUILayout.Width(58f)))
                    _selectedTask = task;
            }

            EditorGUILayout.LabelField("State", task.State.ToString());
            EditorGUILayout.LabelField("NodeId", string.IsNullOrEmpty(task.NodeId) ? "-" : task.NodeId);
            EditorGUILayout.LabelField("Dependency", string.IsNullOrEmpty(task.DependencyKey) ? "-" : task.DependencyKey);

            var result = task.Result;
            if (result != null && result.IsCompleted)
            {
                EditorGUILayout.LabelField("Outcome", result.Outcome.ToString());
                if (result.FailReason != TaskFailReason.None)
                    EditorGUILayout.LabelField("FailReason", result.FailReason.ToString());
                if (result.CancelReason != TaskCancelReason.None)
                    EditorGUILayout.LabelField("CancelReason", result.CancelReason.ToString());
                if (!string.IsNullOrEmpty(result.ErrorCode))
                    EditorGUILayout.LabelField("ErrorCode", result.ErrorCode);
            }
        }

        GUI.backgroundColor = old;
    }

    private void RebuildViewData()
    {
        _queues.Clear();
        var service = TaskService.Instance;
        if (service == null)
            return;

        var snapshot = service.GetQueuesSnapshot();
        foreach (var q in snapshot)
            if (q != null) _queues.Add(q);

        if (_selectedQueue != null && !_queues.Contains(_selectedQueue))
        {
            _selectedQueue = _queues.Count > 0 ? _queues[0] : null;
            _selectedTask = _selectedQueue?.Current;
        }

        var visibleTaskIds = new HashSet<int>();
        foreach (var q in _queues)
        {
            if (q.Current != null)
            {
                int id = q.Current.GetHashCode();
                visibleTaskIds.Add(id);
                TrackTask(q.Current, q.QueueName);
            }

            foreach (var task in q.GetPendingSnapshot())
            {
                if (task == null) continue;
                int id = task.GetHashCode();
                visibleTaskIds.Add(id);
                TrackTask(task, q.QueueName);
            }
        }

        RemoveInvisibleFinishedTracks(visibleTaskIds);
    }

    private void TrackTask(TaskBase task, string queueName)
    {
        int id = task.GetHashCode();
        string resultSignature = BuildResultSignature(task.Result);

        if (!_tracks.TryGetValue(id, out var track))
        {
            _tracks[id] = new TaskTrack
            {
                TaskRef = task,
                QueueName = queueName,
                LastState = task.State,
                LastResultSignature = resultSignature
            };

            AddLog($"{queueName} / {GetTaskTitle(task)} discovered @ {task.State}");
            return;
        }

        if (track.LastState != task.State || track.LastResultSignature != resultSignature)
        {
            AddLog($"{queueName} / {GetTaskTitle(task)} : {track.LastState} -> {task.State}{BuildReasonText(task.Result)}");
            track.LastState = task.State;
            track.LastResultSignature = resultSignature;
        }
    }

    private void RemoveInvisibleFinishedTracks(HashSet<int> visibleTaskIds)
    {
        var remove = new List<int>();
        foreach (var kv in _tracks)
        {
            if (visibleTaskIds.Contains(kv.Key))
                continue;

            if (kv.Value.TaskRef == null || kv.Value.TaskRef.IsDone)
                remove.Add(kv.Key);
        }

        foreach (var id in remove)
            _tracks.Remove(id);
    }

    private void AddLog(string text)
    {
        _logs.Add(new TraceEntry { Time = DateTime.Now, Text = text });
        while (_logs.Count > MaxLogCount)
            _logs.RemoveAt(0);
    }

    private static string BuildReasonText(TaskResult result)
    {
        if (result == null) return string.Empty;
        if (result.Outcome == TaskOutcome.Failed)
            return $" (Fail: {result.FailReason}{(string.IsNullOrEmpty(result.Message) ? "" : " / " + result.Message)})";
        if (result.Outcome == TaskOutcome.Canceled)
            return $" (Cancel: {result.CancelReason}{(string.IsNullOrEmpty(result.Message) ? "" : " / " + result.Message)})";
        if (result.Outcome == TaskOutcome.Succeeded && !string.IsNullOrEmpty(result.Message))
            return $" (Success: {result.Message})";
        return string.Empty;
    }

    private static string BuildResultSignature(TaskResult result)
    {
        if (result == null) return "null";
        return $"{result.Outcome}|{result.FailReason}|{result.CancelReason}|{result.ErrorCode}|{result.Message}|{result.Duration:F3}";
    }

    private static string GetTaskTitle(TaskBase task)
    {
        return string.IsNullOrEmpty(task.Name) ? task.GetType().Name : task.Name;
    }

    private static void DrawMiniStat(string label, string value)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.MinWidth(110f)))
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(value);
        }
    }

    private static Color GetTaskColor(TaskState state)
    {
        return state switch
        {
            TaskState.Pending => new Color(0.75f, 0.75f, 0.75f),
            TaskState.Running => new Color(0.45f, 0.72f, 0.95f),
            TaskState.Succeeded => new Color(0.50f, 0.85f, 0.55f),
            TaskState.Failed => new Color(0.95f, 0.50f, 0.50f),
            TaskState.Canceled => new Color(0.95f, 0.78f, 0.40f),
            _ => new Color(0.85f, 0.85f, 0.85f)
        };
    }

    private sealed class TaskTrack
    {
        public TaskBase TaskRef;
        public string QueueName;
        public TaskState LastState;
        public string LastResultSignature;
    }

    private sealed class TraceEntry
    {
        public DateTime Time;
        public string Text;
    }
}
#endif
