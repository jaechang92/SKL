using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using Metamorph.Managers;
using System;
using System.Threading;
using UnityEngine;


// ==================================================
// 통합 초기화 매니저 (옵션)
// ==================================================
public class GameDataInitializer : MonoBehaviour, IInitializableAsync
{
    public string Name => "Game Data Initializer";
    public InitializationPriority Priority { get; set; } = InitializationPriority.Critical;

    public bool IsInitialized { get; private set; }

    public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            JCDebug.Log("[GameDataInitializer] 게임 데이터 시스템 초기화 시작");

            // 1. DataManager 초기화
            await PlayerDataManager.Instance.InitializeAsync(cancellationToken);

            // 2. SaveManager 초기화 (데이터 로드 포함)
            await UniTaskSaveManager.Instance.InitializeAsync(cancellationToken);

            // 3. 자동 저장 시작
            UniTaskSaveManager.Instance.StartAutoSave(PlayerDataManager.Instance);

            IsInitialized = true;
            JCDebug.Log("[GameDataInitializer] 게임 데이터 시스템 초기화 완료");
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[GameDataInitializer] 게임 데이터 시스템 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
    }
}