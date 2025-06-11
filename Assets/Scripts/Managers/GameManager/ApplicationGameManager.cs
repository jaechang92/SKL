// ====== 11. 게임 매니저 (MVP 패턴의 Model) ======
using Metamorph.Managers;
using System;
using System.Collections;
using UnityEngine;

public class ApplicationGameManager : SingletonManager<ApplicationGameManager>
{
    [Header("Game State")]
    public GameState currentGameState;

    public event Action<GameState> OnGameStateChanged;

    protected override void OnCreated()
    {
        base.OnCreated();
        InitializeGame();
    }

    private void InitializeGame()
    {
        currentGameState = GameState.Loading;
        Debug.Log("ApplicationGameManager initialized in GameScene");

        // 게임 씬에서의 초기화 로직
        StartCoroutine(InitializeGameScene());
    }

    private IEnumerator InitializeGameScene()
    {
        // 플레이어 데이터 적용
        yield return StartCoroutine(ApplyPlayerData());

        // UI 초기화
        yield return StartCoroutine(InitializeGameUI());

        // 게임 시작 준비 완료
        ChangeGameState(GameState.Ready);
    }

    private IEnumerator ApplyPlayerData()
    {
        PlayerData playerData = UniTaskSaveDataManager.Instance.PlayerData;
        if (playerData != null)
        {
            // 플레이어 레벨, 스킬, 아이템 등 적용
            Debug.Log($"Applied player data: Level {playerData.level}, Gold {playerData.gold}");
        }
        yield return null;
    }

    private IEnumerator InitializeGameUI()
    {
        // UI 초기화 로직
        Debug.Log("Game UI initialized");
        yield return null;
    }

    public void ChangeGameState(GameState newState)
    {
        if (currentGameState != newState)
        {
            GameState previousState = currentGameState;
            currentGameState = newState;
            OnGameStateChanged?.Invoke(newState);
            Debug.Log($"Game state changed from {previousState} to {newState}");
        }
    }
}

// ====== 12. 게임 상태 열거형 ======
public enum GameState
{
    Loading,
    Ready,
    Playing,
    Paused,
    GameOver,
    Victory
}