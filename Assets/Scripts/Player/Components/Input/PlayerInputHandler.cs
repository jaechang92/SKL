// Assets/Scripts/Player/Components/Input/PlayerInputHandler.cs
using UnityEngine;
using UnityEngine.InputSystem;
using Metamorph.Core.Interfaces;
using System.Collections.Generic;
using System;

namespace Metamorph.Player.Components
{
    public class PlayerInputHandler : MonoBehaviour
    {
        private PlayerInputActions _inputActions;
        private Dictionary<InputAction, System.Action<InputAction.CallbackContext>> _inputBindings;

        // 의존성 주입된 인터페이스들
        private IMoveable _moveable;
        private ISkillUser _skillUser;
        private IFormChangeable _formChangeable;

        public void Initialize(IMoveable moveable, ISkillUser skillUser, IFormChangeable formChangeable)
        {
            _moveable = moveable;
            _skillUser = skillUser;
            _formChangeable = formChangeable;

            _inputActions = new PlayerInputActions();
            InitializeInputBindings();
        }

        private void InitializeInputBindings()
        {
            _inputBindings = new Dictionary<InputAction, System.Action<InputAction.CallbackContext>>
            {
                { _inputActions.Player.BasicAttack, OnBasicAttackPerformed },
                { _inputActions.Player.Jump, OnJumpPerformed },
                { _inputActions.Player.Dash, OnDashPerformed },
                { _inputActions.Player.Skill1, OnSkill1Performed },
                { _inputActions.Player.Skill2, OnSkill2Performed },
                { _inputActions.Player.Skill3, OnSkill3Performed },

                // 새로운 입력이 필요하면 여기에만 추가하면 됨!
            };
        }

        private void OnSkill3Performed(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        private void OnSkill2Performed(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        private void OnSkill1Performed(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        private void OnBasicAttackPerformed(InputAction.CallbackContext context)
        {
            throw new NotImplementedException();
        }

        private void OnJumpPerformed(InputAction.CallbackContext context)
        {
            _moveable?.Jump(10f); // 점프 힘은 설정값으로 관리
        }
        private void OnDashPerformed(InputAction.CallbackContext context)
        {
           _moveable?.Dash(10f); // 대시 힘은 설정값으로 관리
        }

        // 기타 입력 처리 메서드들...
    }
}