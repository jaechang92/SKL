using UnityEngine;

// 포탈 트리거 컴포넌트
public class Portal : MonoBehaviour
{
    [SerializeField] private Transform exitPoint;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            PortalManager.TeleportPlayer(exitPoint.position, other.transform);
        }
    }
}