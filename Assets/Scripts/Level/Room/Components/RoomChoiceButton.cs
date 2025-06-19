using Metamorph.Level.Generation;
using Metamorph.Level.Room;
using UnityEngine.UI;
using UnityEngine;

/// <summary>
/// 방 선택 버튼
/// </summary>
public class RoomChoiceButton : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button _button;
    [SerializeField] private Image _roomIcon;
    [SerializeField] private Text _roomName;
    [SerializeField] private Text _roomDescription;

    private RoomChoicePortal _associatedPortal;
    private RoomChoiceManager _roomChoiceManager;

    public void Initialize(RoomChoicePortal portal, RoomChoiceManager manager)
    {
        _associatedPortal = portal;
        _roomChoiceManager = manager;

        SetupButton();
        UpdateUI();
    }

    private void SetupButton()
    {
        if (_button != null)
        {
            _button.onClick.AddListener(() => {
                _associatedPortal?.OnSelected();
            });
        }
    }

    private void UpdateUI()
    {
        if (_associatedPortal == null) return;

        // 방 이름
        if (_roomName != null)
        {
            _roomName.text = GetRoomDisplayName(_associatedPortal.RoomType);
        }

        // 방 설명
        if (_roomDescription != null)
        {
            _roomDescription.text = GetRoomDescription(_associatedPortal.RoomType);
        }

        // 방 아이콘
        if (_roomIcon != null)
        {
            var iconSprite = Resources.Load<Sprite>($"Icons/RoomType_{_associatedPortal.RoomType}");
            if (iconSprite != null)
            {
                _roomIcon.sprite = iconSprite;
            }
        }
    }

    private string GetRoomDisplayName(RoomType roomType)
    {
        return roomType switch
        {
            RoomType.Normal => "일반 방",
            RoomType.Reward => "보상 방",
            RoomType.Boss => "보스 방",
            _ => "알 수 없는 방"
        };
    }

    private string GetRoomDescription(RoomType roomType)
    {
        return roomType switch
        {
            RoomType.Normal => "일반적인 적들과 전투",
            RoomType.Reward => "보상을 얻을 수 있는 방",
            RoomType.Boss => "강력한 보스와의 전투",
            _ => ""
        };
    }
}