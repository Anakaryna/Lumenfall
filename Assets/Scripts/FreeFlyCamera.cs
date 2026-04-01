using UnityEngine;

public class FreeFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float sprintMultiplier = 2f;
    public float verticalSpeed = 8f;

    [Header("Mouse")]
    public float mouseSensitivity = 2.5f;
    public float smoothTime = 0.05f;

    private float rotationX;
    private float rotationY;

    private float currentVelX;
    private float currentVelY;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleMouseLook();
        HandleMovement();
        HandleCursorUnlock();
    }

    void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;

        rotationX -= mouseY;
        rotationY += mouseX;

        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        float smoothX = Mathf.SmoothDampAngle(transform.eulerAngles.x, rotationX, ref currentVelX, smoothTime);
        float smoothY = Mathf.SmoothDampAngle(transform.eulerAngles.y, rotationY, ref currentVelY, smoothTime);

        transform.rotation = Quaternion.Euler(smoothX, smoothY, 0f);
    }

    void HandleMovement()
    {
        float speed = moveSpeed;

        if (Input.GetKey(KeyCode.LeftShift))
            speed *= sprintMultiplier;

        // ZQSD (AZERTY)
        float forward = 0f;
        float right = 0f;

        if (Input.GetKey(KeyCode.Z)) forward += 1f;
        if (Input.GetKey(KeyCode.S)) forward -= 1f;
        if (Input.GetKey(KeyCode.D)) right += 1f;
        if (Input.GetKey(KeyCode.Q)) right -= 1f;

        Vector3 move = transform.forward * forward + transform.right * right;

        // Vertical (A = down, E = up) 
        if (Input.GetKey(KeyCode.E))
            move += Vector3.up;
        if (Input.GetKey(KeyCode.A))
            move += Vector3.down;

        transform.position += move * speed * Time.deltaTime;
    }

    void HandleCursorUnlock()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}