using UnityEngine;
using System.Collections;

public class CameraController : MonoBehaviour
{
    // TODO following an object should disable all other movements
    // TODO Speed should be relative to how high the camera is?
    // TODO Movements should be more smooth

    // The object that the camera would follow
    public GameObject targetObj;
    // Distance away from the targetObject
    Vector3 offset;
    // Camera movement for XYZ
    public float cameraSpeed = 2f;
    public float altCameraSpeed = 0.15f;
    // Camera Zooming 
    public float zoomMin = 5f;
    public float zoomMax = 75f;
    public float zoomSpeed = 5f;
    // Rotation 
    public float turnSpeedLR = 2f;
    public float turnSpeedUD = 25f;
    float turnRotLR = 0f;
    // The final position and rotation for every frame
    Vector3 targetPos;
    Quaternion targetRot;
    public float smoothness = .5f;
    // Special camera conditions such as slowing the camera down
    bool isAltCamera = false;

    void Awake()
    {
        targetPos = transform.position;
        targetRot = transform.rotation;
    }

    void Update()
    {
        // If following target object
        if (targetObj != null)
        {
            transform.position = targetObj.transform.position + offset;
        }
        else
        {
            Move();
            Rotate();
        }
    }
    // XYZ axis movement
    void Move()
    {
        // X and Z axis movement
        float moveX = Input.GetAxis("Horizontal") * cameraSpeed;
        float moveZ = Input.GetAxis("Vertical") * cameraSpeed;
        //Y axis movement & is relative to camera's height // Want it to scale with height? use transform.position.y / 15
        float moveY = -Input.mouseScrollDelta.y * zoomSpeed;
        //Creating the movement vector to use 
        Vector3 movement = new Vector3(moveX, moveY, moveZ);
        movement = transform.TransformDirection(movement);

        targetPos.Set(targetPos.x + movement.x, Mathf.Clamp(targetPos.y + movement.y, zoomMin, zoomMax), targetPos.z + movement.z);

        //Holding shift doubles camera speed
        if (Input.GetKey(KeyCode.LeftShift))
        {
            targetPos.Set(targetPos.x + movement.x, targetPos.y + movement.y, targetPos.z + movement.z);
        }
        
        // The actual movement
        transform.position = Vector3.Lerp(transform.position, targetPos, (1 - smoothness));

        // Pressing TAB to enter cinematic mode
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (isAltCamera == false)
            {
                Cursor.visible = false;
                cameraSpeed = altCameraSpeed;
                isAltCamera = true;
            }
            else
            {
                Cursor.visible = true;
                cameraSpeed = 2f;
                isAltCamera = false;
            }
        }
    }
    // left/right/up/down rotating & resetting the front direction
    void Rotate()
    {
        // Turning camera left & right
        if (Input.GetKey(KeyCode.Q))
        {
            turnRotLR -= turnSpeedLR * Time.deltaTime;
        }
        else if (Input.GetKey(KeyCode.E))
        {
            turnRotLR += turnSpeedLR * Time.deltaTime;
        }

        // Changing rotation to make camera angle proportional to camera height, and turning it left or right 
        // Changes the Y axis angle direction of parent object to have it move in the correct direction
        transform.rotation = Quaternion.Euler(0, turnRotLR, 0f);

        // Changes camera and its parent object X and Y axis angles for making angle relative to height and turning the head left/right/up/down
        // Left/right rotation of parent object
        //transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(transform.rotation.x, turnRotLR, 0f), turnSpeedLR);
    }
}
