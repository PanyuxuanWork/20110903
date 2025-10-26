// FreeCameraController.cs（使用旧输入系统替代 InputSystem）
using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 10f;
    public float rotateSpeed = 100f;
    public float zoomSpeed = 10f;
    public float minZoomDistance = 5f;
    public float maxZoomDistance = 50f;
    public Camera mainCamera;

    private Vector3 lastMousePosition;

    private void Update()
    {
        // 鼠标右键旋转
        if (Input.GetMouseButton(1))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            RotateCamera(new Vector2(delta.x, delta.y));
        }

        // 滚轮缩放
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            ZoomCamera(scroll * 10f); // 放大滚轮响应速度
        }

        // 键盘移动
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        if (Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f)
        {
            MoveCamera(new Vector2(h, v));
        }

        lastMousePosition = Input.mousePosition;
    }

    private void RotateCamera(Vector2 delta)
    {
        mainCamera.transform.RotateAround(mainCamera.transform.position, Vector3.up, delta.x * rotateSpeed * Time.deltaTime);

        float pitchDelta = -delta.y * rotateSpeed * Time.deltaTime;
        Vector3 currentEulerAngles = mainCamera.transform.eulerAngles;
        float currentPitch = currentEulerAngles.x;
        if (currentPitch > 180) currentPitch -= 360;
        float newPitch = Mathf.Clamp(currentPitch + pitchDelta, -80f, 80f);
        currentEulerAngles.x = newPitch;
        mainCamera.transform.eulerAngles = currentEulerAngles;
    }

    private void ZoomCamera(float scroll)
    {
        Vector3 newPosition = mainCamera.transform.position + mainCamera.transform.forward * scroll * zoomSpeed * Time.deltaTime;
        float distance = Vector3.Distance(newPosition, mainCamera.transform.position);
        if (distance >= minZoomDistance && distance <= maxZoomDistance)
        {
            mainCamera.transform.position = newPosition;
        }
    }

    private void MoveCamera(Vector2 input)
    {
        Vector3 moveDirection = new Vector3(input.x, 0, input.y);
        mainCamera.transform.Translate(moveDirection * moveSpeed * Time.deltaTime, Space.Self);
    }
}
