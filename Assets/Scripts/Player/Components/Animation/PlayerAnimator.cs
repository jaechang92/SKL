// Assets/Scripts/Player/Components/Animation/PlayerAnimator.cs
using UnityEngine;

namespace Metamorph.Player.Components
{
    public class PlayerAnimator : MonoBehaviour
    {
        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        public void UpdateMovementAnimation(float speed, bool isGrounded)
        {
            if (_animator?.runtimeAnimatorController == null) return;

            _animator.SetFloat("HorizontalSpeed", Mathf.Abs(speed));
            _animator.SetBool("IsGrounded", isGrounded);
        }

        public void UpdateAnimatorController(RuntimeAnimatorController controller)
        {
            if (_animator != null)
            {
                _animator.runtimeAnimatorController = controller;
            }
        }
    }
}