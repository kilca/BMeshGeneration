using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public float moveSpeed = 0.1f;     // Speed of camera movement
    public float rotateSpeed = 100f;  // Speed of camera rotation
    public float zoomSpeed = 2f;      // Speed of camera zoom
    public float minZoom = 5f;        // Minimum zoom distance
    public float maxZoom = 50f;       // Maximum zoom distance

    private Vector3 lastMousePosition;
    private Camera camera;

    private void Start()
    {
        camera = Camera.main;
        lastMousePosition = Input.mousePosition;
    }

    private void Update()
    {
        HandleMouseDrag();
        HandleMouseScroll();
    }

    private void HandleMouseDrag()
    {
        if (Input.GetMouseButton(1))  // Check if the right mouse button is held down
        {
            Vector3 deltaMousePosition = Input.mousePosition - lastMousePosition;
            lastMousePosition = Input.mousePosition;

            // Rotate the camera around the origin
            float horizontalRotation = deltaMousePosition.x * rotateSpeed * Time.deltaTime;
            float verticalRotation = -deltaMousePosition.y * rotateSpeed * Time.deltaTime;

            transform.RotateAround(Vector3.zero, Vector3.up, horizontalRotation);
            transform.RotateAround(Vector3.zero, transform.right, verticalRotation);
        }
        else
        {
            lastMousePosition = Input.mousePosition;
        }
    }

    private void HandleMouseScroll()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (scroll != 0f)
        {
            float zoomAmount = -scroll * zoomSpeed;

            // Calculate new position
            Vector3 direction = (transform.position - Vector3.zero).normalized;
            transform.position += direction * zoomAmount;

            // Clamp the zoom
            float distanceToOrigin = Vector3.Distance(transform.position, Vector3.zero);
            if (distanceToOrigin < minZoom)
            {
                transform.position = Vector3.zero + direction * minZoom;
            }
            else if (distanceToOrigin > maxZoom)
            {
                transform.position = Vector3.zero + direction * maxZoom;
            }
        }
    }
}