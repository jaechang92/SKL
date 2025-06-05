using System;
using UnityEngine;

// 포탈 매니저 (이벤트 발생 주체)
public class PortalManager : MonoBehaviour
{
    public static event Action<Vector3, Transform> OnPortalUsed;

    public static void TeleportPlayer(Vector3 exitPosition, Transform player)
    {
        // 플레이어 텔레포트 로직
        player.position = exitPosition;

        // 카메라에게 알림 (Observer Pattern)
        OnPortalUsed?.Invoke(exitPosition, player);
    }
}