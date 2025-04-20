// File: CharacterMovement.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterData))]
public class CharacterMovement : MonoBehaviour
{
    // --- Component References ---
    private Rigidbody2D rb;
    private CharacterData characterData;
    private CharacterCombat combat;

    [Header("Jumping & Ground Check")]
    [Tooltip("La fuerza de impulso vertical aplicada al saltar. Controla la altura inicial del salto.")]
    public float jumpForce = 10f; // <--- ¡MODIFICA ESTE VALOR EN EL INSPECTOR DEL PREFAB!
    [Tooltip("Un Transform posicionado a los pies del personaje para chequear el suelo.")]
    public Transform groundCheckPoint;
    [Tooltip("El radio del círculo usado para la detección de suelo.")]
    public float groundCheckRadius = 0.2f;
    [Tooltip("Qué capas se consideran suelo.")]
    public LayerMask groundLayer;

    // --- State ---
    private bool isGrounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        characterData = GetComponent<CharacterData>();
        combat = GetComponent<CharacterCombat>();

        if (rb == null) Debug.LogError("Rigidbody2D not found!", this);
        if (characterData == null) Debug.LogError("CharacterData not found!", this);
        if (combat == null) Debug.LogWarning("CharacterCombat not found! Animator visuals might not update.", this);

        if (groundCheckPoint == null) {
            Transform foundGroundCheck = transform.Find("GroundCheck");
            if (foundGroundCheck != null) { groundCheckPoint = foundGroundCheck; }
            else {
                GameObject groundCheckObj = new GameObject("GroundCheck");
                groundCheckObj.transform.SetParent(transform);
                float colliderHeight = GetComponent<Collider2D>()?.bounds.extents.y ?? 0.5f;
                groundCheckObj.transform.localPosition = new Vector3(0, -colliderHeight - 0.05f, 0);
                groundCheckPoint = groundCheckObj.transform;
                Debug.LogWarning($"GroundCheckPoint not assigned on {gameObject.name}. Created one automatically. Adjust its position if needed.", this);
            }
        }
        if (groundLayer == 0) { Debug.LogError($"Ground Layer mask not assigned in the Inspector for {gameObject.name}! Ground check will fail.", this); }
    }

    void Update()
    {
        CheckIfGrounded();
        UpdateAnimatorVisuals();
    }

    private void CheckIfGrounded()
    {
        if (groundCheckPoint == null || characterData == null || characterData.isStunned) { isGrounded = false; return; }
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }

    public bool Jump()
    {
        if (isGrounded && characterData != null && !characterData.isStunned && !characterData.isDashing && !characterData.isBlocking) {
            if(rb != null) {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
                isGrounded = false;
                combat?.SetAnimatorTrigger("Jump");
                return true;
            }
        }
        return false;
    }

    public bool IsGrounded() { return isGrounded; }

    private void UpdateAnimatorVisuals() {
        if (combat != null && rb != null) { combat.UpdateAnimatorLogic(isGrounded, rb.linearVelocity); }
    }

    void OnDrawGizmosSelected() {
        if (groundCheckPoint != null) { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius); }
    }
}