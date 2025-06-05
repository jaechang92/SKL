using UnityEngine;


// 포탈 및 기타 이벤트 처리 (Observer Pattern 적용)
public class CameraEventHandler
{
    private CameraController cameraController;

    public CameraEventHandler(CameraController controller)
    {
        cameraController = controller;
    }

    public void RegisterPortalEvents()
    {
        PortalManager.OnPortalUsed += HandlePortalTeleport;
    }

    public void UnregisterPortalEvents()
    {
        PortalManager.OnPortalUsed -= HandlePortalTeleport;
    }

    private void HandlePortalTeleport(Vector3 portalExitPosition, Transform player)
    {
        cameraController.SetTargetImmediate(player, portalExitPosition);
    }
}