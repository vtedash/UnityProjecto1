// File: CharacterMovement.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(CharacterCombat))] // Necesario para trigger de animación
public class CharacterMovement : MonoBehaviour
{
    // --- Component References ---
    private Rigidbody2D rb;
    private CharacterData characterData;
    private CharacterCombat combat; // Necesario para trigger de animación

    [Header("Jumping & Ground Check")]
    [Tooltip("La fuerza de impulso vertical aplicada al saltar. Controla la altura inicial del salto.")]
    public float jumpForce = 10f;
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
        combat = GetComponent<CharacterCombat>(); // Obtener Combat

        // --- Component Validation ---
        if (rb == null) Debug.LogError("Rigidbody2D not found!", this);
        if (characterData == null) Debug.LogError("CharacterData not found!", this);
        if (combat == null) Debug.LogWarning("CharacterCombat not found! Animator visuals might not update correctly.", this); // Advertencia ahora

        // --- Ground Check Point Auto-Setup ---
        if (groundCheckPoint == null) {
            // ... (lógica de auto-setup sin cambios)
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
        // --- Ground Layer Validation ---
        if (groundLayer == 0) {
             Debug.LogError($"Ground Layer mask not assigned in the Inspector for {gameObject.name}! Ground check will fail.", this);
        }
    }

    void Update()
    {
        CheckIfGrounded();
        UpdateAnimatorVisuals(); // Llamada centralizada a la lógica del animator
    }

    /// <summary> Verifica si el personaje está tocando el suelo. </summary>
    private void CheckIfGrounded()
    {
        if (groundCheckPoint == null || characterData == null || characterData.isStunned) {
            isGrounded = false;
            return;
        }
        // Usa OverlapCircle para detectar colisiones con la capa de suelo
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }

    /// <summary> Intenta realizar un salto. </summary>
    /// <returns>True si el salto se inició con éxito.</returns>
    public bool Jump()
    {
        // Solo puede saltar si está en el suelo y no está incapacitado (stun, dash, block)
        if (isGrounded && characterData != null && !characterData.isStunned && !characterData.isDashing && !characterData.isBlocking)
        {
            if(rb != null)
            {
                // Resetea velocidad vertical para saltos consistentes
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                // Aplica fuerza de salto
                rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
                isGrounded = false; // Ya no está en el suelo
                // *** ¡Dispara el trigger de animación de salto! ***
                combat?.SetAnimatorTrigger("Jump"); // Llama al helper en CharacterCombat
                return true; // Salto iniciado
            }
        }
        return false; // No se cumplieron las condiciones para saltar
    }

    /// <summary> Devuelve si el personaje está actualmente en el suelo. </summary>
    public bool IsGrounded()
    {
        return isGrounded;
    }

    /// <summary> Actualiza los parámetros del Animator relacionados con el movimiento. </summary>
    private void UpdateAnimatorVisuals()
    {
        // Llama al método centralizado en CharacterCombat para actualizar el Animator
        if (combat != null && rb != null)
        {
            combat.UpdateAnimatorLogic(isGrounded, rb.linearVelocity);
        }
    }

    /// <summary> Dibuja el gizmo de chequeo de suelo en el editor. </summary>
    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
}