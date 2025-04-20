// File: CharacterMovement.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterData))]
public class CharacterMovement : MonoBehaviour
{
    // --- Component References ---
    private Rigidbody2D rb;
    private CharacterData characterData;
    private CharacterCombat combat; // Needed to update animator visuals related to movement

    [Header("Jumping & Ground Check")]
    [Tooltip("The force applied upwards when jumping.")]
    public float jumpForce = 10f;
    [Tooltip("A Transform positioned at the character's feet to check for ground.")]
    public Transform groundCheckPoint;
    [Tooltip("The radius of the circle used for ground detection.")]
    public float groundCheckRadius = 0.2f;
    [Tooltip("Which layers should be considered ground.")]
    public LayerMask groundLayer;

    // --- State ---
    private bool isGrounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        characterData = GetComponent<CharacterData>();
        combat = GetComponent<CharacterCombat>(); // Get combat reference

        // --- Component Validation ---
        if (rb == null) Debug.LogError("Rigidbody2D not found!", this);
        if (characterData == null) Debug.LogError("CharacterData not found!", this);
        if (combat == null) Debug.LogWarning("CharacterCombat not found! Animator visuals might not update.", this); // Warning is ok

        // --- Ground Check Point Auto-Setup ---
        if (groundCheckPoint == null)
        {
            Transform foundGroundCheck = transform.Find("GroundCheck");
            if (foundGroundCheck != null) {
                groundCheckPoint = foundGroundCheck;
                Debug.Log($"Found existing GroundCheck object on {gameObject.name}.", this);
            } else {
                GameObject groundCheckObj = new GameObject("GroundCheck");
                groundCheckObj.transform.SetParent(transform);
                // Try to position based on collider bounds, default to 0.5f height + offset
                float colliderHeight = GetComponent<Collider2D>()?.bounds.extents.y ?? 0.5f;
                groundCheckObj.transform.localPosition = new Vector3(0, -colliderHeight - 0.05f, 0);
                groundCheckPoint = groundCheckObj.transform;
                Debug.LogWarning($"GroundCheckPoint not assigned on {gameObject.name}. Created one automatically. Adjust its position if needed.", this);
            }
        }

        // Ensure the ground layer is assigned
        if (groundLayer == 0) // LayerMask value is 0 if unassigned
        {
            Debug.LogError($"Ground Layer mask not assigned in the Inspector for {gameObject.name}! Ground check will fail.", this);
        }
    }

    void Update()
    {
        // Check ground status every frame for accurate info
        CheckIfGrounded();
        // Update animator visuals based on current state (moved from AI)
        UpdateAnimatorVisuals();
    }

    // FixedUpdate is no longer needed here as horizontal movement is handled by AIPath/AIController interaction

    /// <summary>
    /// Checks if the character is currently touching the ground using an OverlapCircle.
    /// </summary>
    private void CheckIfGrounded()
    {
        // Cannot be grounded if stunned, missing components, or ground check point is null
        if (groundCheckPoint == null || characterData == null || characterData.isStunned)
        {
            isGrounded = false;
            return;
        }
        // Perform the physics check
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }

    /// <summary>
    /// Attempts to make the character jump.
    /// </summary>
    /// <returns>True if the jump was successful, false otherwise.</returns>
    public bool Jump()
    {
        // Conditions for jumping: Must be grounded, not stunned, not dashing, not blocking
        if (isGrounded && characterData != null && !characterData.isStunned && !characterData.isDashing && !characterData.isBlocking)
        {
            if(rb != null)
            {
                // Reset vertical velocity before applying impulse for consistent jump height
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                // Apply the jump force
                rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
                // Immediately set grounded to false (avoids double jumps)
                isGrounded = false;
                // Trigger jump animation (via CharacterCombat helper)
                combat?.SetAnimatorTrigger("Jump"); // Use "?." for safety if combat is missing
                return true; // Jump successful
            }
        }
        return false; // Jump failed (conditions not met)
    }

    /// <summary>
    /// Returns whether the character is currently considered grounded.
    /// </summary>
    public bool IsGrounded()
    {
        return isGrounded;
    }

    /// <summary>
    /// Updates the Animator parameters related to movement state.
    /// Now called locally from Update.
    /// </summary>
    private void UpdateAnimatorVisuals()
    {
        if (combat != null && rb != null) // Check dependencies
        {
            combat.UpdateAnimatorLogic(isGrounded, rb.linearVelocity);
        }
    }


    // --- Gizmos ---
    /// <summary>
    /// Draws a wire sphere in the Scene view to visualize the ground check area.
    /// </summary>
    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
}