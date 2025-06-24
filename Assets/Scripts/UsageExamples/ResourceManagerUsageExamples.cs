// ============================================================================
// GameResourceManager 설정 및 사용 가이드
// ============================================================================

/*
=== 1. ResourceSettings ScriptableObject 생성 방법 ===

1) Unity 에디터에서 우클릭
2) Create > Metamorph > Managers > Resource Settings
3) 이름을 "GameResourceSettings"로 변경
4) 아래 기본값들로 설정:

Essential Resources (필수 리소스들):
- UI/LoadingScreen (UI, Critical, Required)
- UI/MainLogo (UI, High)
- Audio/BGM/MainTheme (Audio, High)
- Audio/SFX/ButtonClick (Audio, Normal)
- Prefabs/UI/DamageText (Prefab, Normal)

Addressable Resources (있다면):
- PlayerCharacter (Prefab, Critical, Required)
- EnemySpawner (Prefab, High)
- SkillEffects (Prefab, Normal)

Scene Preload Settings:
- MainMenu: UI/MenuBackground, Audio/BGM/Menu
- Gameplay: Prefabs/Player, Prefabs/Enemies
- Boss: Audio/BGM/Boss, Prefabs/BossEffects
*/

// ============================================================================
// 2. GameResourceManager 사용 예시들
// ============================================================================

using CustomDebug;
using Metamorph.Managers;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 각 부분에서 리소스 매니저 사용하는 방법들
/// </summary>
public class ResourceManagerUsageExamples : MonoBehaviour
{
    // === 기본 리소스 로딩 ===
    public async void LoadBasicResources()
    {
        var resourceManager = GameResourceManager.Instance;

        // 텍스처 로딩
        var logoTexture = await resourceManager.LoadAsync<Texture2D>("UI/MainLogo");
        if (logoTexture != null)
        {
            // 로고 이미지 사용
            GetComponent<Image>().sprite = Sprite.Create(logoTexture,
                new Rect(0, 0, logoTexture.width, logoTexture.height),
                Vector2.one * 0.5f);
        }

        // 오디오 클립 로딩
        var bgmClip = await resourceManager.LoadAsync<AudioClip>("Audio/BGM/MainTheme");
        if (bgmClip != null)
        {
            AudioManager.Instance.PlaySFX(bgmClip);
        }

        // 프리팹 로딩 및 인스턴스화
        var playerPrefab = await resourceManager.LoadAsync<GameObject>("Prefabs/Player");
        if (playerPrefab != null)
        {
            var player = Instantiate(playerPrefab);
            // 플레이어 설정...
        }
    }

    // === 여러 리소스 동시 로딩 ===
    public async void LoadMultipleResources()
    {
        var resourceManager = GameResourceManager.Instance;

        var uiPaths = new List<string>
        {
            "UI/HealthBar",
            "UI/ManaBar",
            "UI/SkillIcons",
            "UI/MenuButton"
        };

        var uiTextures = await resourceManager.LoadMultipleAsync<Texture2D>(uiPaths);

        foreach (var kvp in uiTextures)
        {
            JCDebug.Log($"UI 텍스처 로드 완료: {kvp.Key}");
            // UI 설정에 사용...
        }
    }

    // === 씬 전환 시 리소스 관리 ===
    public async void OnSceneTransition(string newSceneName)
    {
        var resourceManager = GameResourceManager.Instance;

        // 씬 변경 알림 (자동으로 이전 씬 리소스 언로드, 새 씬 리소스 로드)
        await resourceManager.OnSceneChanged(newSceneName);

        // 현재 씬의 리소스 사용량 확인
        var (resourceCount, addressableCount) = resourceManager.GetCurrentSceneResourceCount();
        JCDebug.Log($"현재 씬 리소스: R{resourceCount}, A{addressableCount}");
    }

    // === 특정 유형 리소스 일괄 로딩 ===
    public async void LoadAudioResources()
    {
        var resourceManager = GameResourceManager.Instance;

        // 모든 오디오 리소스 로딩
        var audioClips = await resourceManager.LoadResourcesByTypeAsync<AudioClip>(PreloadResourceType.Audio);

        JCDebug.Log($"오디오 리소스 {audioClips.Count}개 로드 완료");

        // AudioManager에 등록
        foreach (var clip in audioClips)
        {
            // AudioManager.Instance.RegisterClip(clip.name, clip);
        }
    }

    // === 우선순위별 로딩 ===
    public async void LoadCriticalResources()
    {
        var resourceManager = GameResourceManager.Instance;

        // Critical 우선순위 리소스만 먼저 로딩
        await resourceManager.LoadResourcesByPriorityAsync(PreloadPriority.Critical);

        JCDebug.Log("게임 진행에 필수적인 리소스 로딩 완료");

        // 게임 시작 가능...
    }

    // === 메모리 관리 ===
    public async void ManageMemory()
    {
        var resourceManager = GameResourceManager.Instance;

        // 현재 메모리 사용량 확인
        JCDebug.Log($"현재 메모리 사용량: {resourceManager.CurrentMemoryUsageMB:F1}MB");

        // 특정 리소스 캐시에서 제거
        resourceManager.ClearCache("UI/OldTexture");

        // 강제 메모리 정리
        if (resourceManager.CurrentMemoryUsageMB > 800f)
        {
            await resourceManager.ForceCleanupMemoryAsync();
        }
    }
}

// ============================================================================
// 3. 이벤트 기반 리소스 관리 예시
// ============================================================================

public class ResourceEventHandler : MonoBehaviour
{
    private void Start()
    {
        var resourceManager = GameResourceManager.Instance;

        // 이벤트 구독
        resourceManager.OnResourceLoaded += OnResourceLoaded;
        resourceManager.OnResourceLoadFailed += OnResourceLoadFailed;
        resourceManager.OnMemoryUsageChanged += OnMemoryUsageChanged;
        resourceManager.OnPreloadCompleted += OnPreloadCompleted;
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제
        if (GameResourceManager.Instance != null)
        {
            var resourceManager = GameResourceManager.Instance;
            resourceManager.OnResourceLoaded -= OnResourceLoaded;
            resourceManager.OnResourceLoadFailed -= OnResourceLoadFailed;
            resourceManager.OnMemoryUsageChanged -= OnMemoryUsageChanged;
            resourceManager.OnPreloadCompleted -= OnPreloadCompleted;
        }
    }

    private void OnResourceLoaded(string path, UnityEngine.Object resource)
    {
        JCDebug.Log($"리소스 로드 완료: {path} ({resource.GetType().Name})");

        // UI 업데이트나 다른 처리...
    }

    private void OnResourceLoadFailed(string path, string error)
    {
        JCDebug.Log($"리소스 로드 실패: {path} - {error}", JCDebug.LogLevel.Warning);

        // 대체 리소스 로드나 오류 처리...
    }

    private void OnMemoryUsageChanged(float memoryMB)
    {
        JCDebug.Log($"메모리 사용량 변경: {memoryMB:F1}MB");

        // 메모리 부족 경고 UI 표시 등...
        if (memoryMB > 900f)
        {
            // 경고 표시
        }
    }

    private void OnPreloadCompleted()
    {
        JCDebug.Log("필수 리소스 프리로드 완료 - 게임 시작 가능");

        // 로딩 화면 숨김, 메인 메뉴 표시 등...
    }
}

// ============================================================================
// 4. Unity Inspector 설정 예시 (GameResourceManager 컴포넌트에서)
// ============================================================================

/*
=== GameResourceManager Inspector 설정 ===

Resource Settings: 위에서 만든 GameResourceSettings ScriptableObject 할당

Memory Threshold MB: 512
Auto Garbage Collection: true
GC Check Interval: 30

Enable Resource Caching: true
Preload Essential Resources: true
Log Resource Operations: true (개발 중), false (배포 시)

Enable Scene Based Loading: true
*/

// ============================================================================
// 5. 성능 최적화 팁들
// ============================================================================

/*
=== 성능 최적화 가이드 ===

1. 프리로드 우선순위 설정:
   - Critical: 게임 시작 필수 (로딩 화면, 플레이어 캐릭터)
   - High: 주요 게임플레이 (UI, 주요 사운드)
   - Normal: 일반적인 리소스
   - Low: 백그라운드 로딩 가능한 것들

2. 씬별 리소스 분리:
   - 각 씬에서만 사용하는 리소스는 Scene Preload Settings에 등록
   - 씬 전환 시 자동으로 언로드되어 메모리 절약

3. Addressables 활용:
   - 큰 리소스들은 Addressables로 관리
   - 번들 최적화와 원격 업데이트 가능

4. 메모리 관리:
   - Memory Threshold를 적절히 설정 (기기별로 다르게)
   - 정기적인 메모리 체크로 자동 정리

5. 로딩 성능:
   - Max Concurrent Loads로 동시 로딩 제한
   - Distribute Preload Across Frames로 프레임 분산
   - Low Priority Delay로 중요하지 않은 리소스 지연 로딩
*/