using UnityEngine;

public class PlayerGroundCheck : MonoBehaviour
{
    private bool _isGrounded;
    public bool IsGrounded { get => _isGrounded; }

    private void FixedUpdate()
    {
        CheckGrounded();
    }

    void CheckGrounded()
    {
        // 레이캐스트로 지면 체크
        RaycastHit2D hit = Physics2D.Raycast(
            transform.position - new Vector3(0, 0.5f, 0),
            Vector2.down,
            0.1f,
            LayerMask.GetMask("Ground")
        );

        _isGrounded = hit.collider != null;
    }
}
