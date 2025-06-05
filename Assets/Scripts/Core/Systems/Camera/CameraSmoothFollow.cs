using UnityEngine;

// 카메라 부드러운 추적 로직 분리
public class CameraSmoothFollow
{
    public Vector3 CalculateDesiredPosition(Vector3 targetPosition, Vector3 offset)
    {
        return targetPosition + offset;
    }

    public Vector3 SmoothMove(Vector3 currentPosition, Vector3 targetPosition, float speed, float deltaTime)
    {
        return Vector3.Lerp(currentPosition, targetPosition, speed * deltaTime);
    }
}