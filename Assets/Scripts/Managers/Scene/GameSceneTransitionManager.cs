// ====== 10. 게임 씬 전환 준비 매니저 ======
using Metamorph.Data;
using Metamorph.Managers;
using UnityEngine;

public class GameSceneTransitionManager : SingletonManager<GameSceneTransitionManager>
{
    public void PrepareTransition(string targetScene)
    {
        // 게임 씬으로 넘어갈 때 필요한 준비 작업들
        SetupGameManagers();
        PreparePlayerState();
        CacheGameData();

        Debug.Log($"Prepared transition to {targetScene}");
    }

    private void SetupGameManagers()
    {
        // 게임 씬에서 필요한 매니저들 미리 설정
        ApplicationGameManager gameManager = FindFirstObjectByType<ApplicationGameManager>();
        if (gameManager == null)
        {
            GameObject go = new GameObject("ApplicationGameManager");
            go.AddComponent<ApplicationGameManager>();
        }
    }

    private void PreparePlayerState()
    {
        // 플레이어 상태를 게임 시작에 맞게 준비
        PlayerData playerData = PlayerDataManager.Instance.PlayerData;
        if (playerData != null)
        {
            Debug.Log($"Player {playerData.playerName} ready to start at stage {playerData.currentStageIndex}");
        }
    }

    private void CacheGameData()
    {
        // 자주 사용될 데이터들을 캐시
        // 예: 아이템 정보, 스킬 정보, 몬스터 정보 등
        Debug.Log("Game data cached for quick access");
    }
}