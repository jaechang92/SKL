using UnityEngine;

// 카메라 경계 체크 로직 분리
public class CameraBoundaryChecker
{
    private Bounds mapBounds;
    private float cameraHalfWidth;
    private float cameraHalfHeight;

    public void SetBoundaries(Bounds bounds, Camera camera)
    {
        mapBounds = bounds;
        cameraHalfHeight = camera.orthographicSize;
        cameraHalfWidth = cameraHalfHeight * camera.aspect;
    }

    public Vector3 ClampToBoundaries(Vector3 position)
    {
        float clampedX = Mathf.Clamp(position.x,
            mapBounds.min.x + cameraHalfWidth,
            mapBounds.max.x - cameraHalfWidth);

        float clampedY = Mathf.Clamp(position.y,
            mapBounds.min.y + cameraHalfHeight,
            mapBounds.max.y - cameraHalfHeight);

        return new Vector3(clampedX, clampedY, position.z);
    }
}