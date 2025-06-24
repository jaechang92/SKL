using Metamorph.Forms.Base;
using Metamorph.Managers;
using UnityEngine;

namespace Metamorph.Forms.Data
{
    // 형태 획득 아이템
    public class FormPickup : MonoBehaviour
    {
        private FormData _formData;

        // 초기화
        public void Initialize(FormData formData)
        {
            _formData = formData;

            // 시각적 표현 업데이트
            SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && _formData != null)
            {
                spriteRenderer.sprite = _formData.formSprite;
            }
        }

        // 플레이어와 충돌 시 형태 획득
        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.CompareTag("Player") && _formData != null)
            {
                // 형태 획득
                FormManager.Instance.UnlockForm(_formData);

                // 아이템 제거
                Destroy(gameObject);
            }
        }
    }
}