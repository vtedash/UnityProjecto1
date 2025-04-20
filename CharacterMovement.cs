// File: CharacterMovement.cs
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterData))]
public class CharacterMovement : MonoBehaviour
{
    private Rigidbody2D rb;
    private CharacterData characterData;

    [Header("Jumping & Ground Check")]
    public float jumpForce = 10f;          // Fuerza del salto (ajustar en Inspector)
    public Transform groundCheckPoint;    // Un GameObject vacío hijo, posicionado a los pies del personaje
    public float groundCheckRadius = 0.2f; // Radio del círculo para detectar suelo (ajustar)
    public LayerMask groundLayer;         // Qué capas se consideran suelo (asignar "Ground" en Inspector)

    private bool isGrounded;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        characterData = GetComponent<CharacterData>();

        // Verificación del Ground Check Point
        if (groundCheckPoint == null)
        {
            Transform foundGroundCheck = transform.Find("GroundCheck");
            if (foundGroundCheck != null) { groundCheckPoint = foundGroundCheck; }
            else
            {
                GameObject groundCheckObj = new GameObject("GroundCheck");
                groundCheckObj.transform.SetParent(transform);
                groundCheckObj.transform.localPosition = new Vector3(0, -(GetComponent<Collider2D>()?.bounds.extents.y ?? 0.5f) - 0.05f, 0);
                groundCheckPoint = groundCheckObj.transform;
                Debug.LogWarning("GroundCheckPoint no asignado en " + gameObject.name + ". Se creó uno automáticamente. Ajusta su posición si es necesario.", this);
            }
        }
        if (rb == null) Debug.LogError("Rigidbody2D no encontrado!", this);
    }

    void Update()
    {
        // Realizar el chequeo de suelo en Update para tener la info lo más actualizada posible
        CheckIfGrounded();
    }

    void FixedUpdate()
    {
        // El movimiento horizontal ahora es controlado por LuchadorAIController.FixedUpdate
        // Este script ya no necesita gestionar el movimiento horizontal básico.
    }

    // Verifica si el personaje está tocando el suelo
    private void CheckIfGrounded()
    {
        if (groundCheckPoint == null || characterData == null || characterData.isStunned) {
            isGrounded = false;
            return;
        }
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }

    // Intenta realizar un salto
    public bool Jump()
    {
        // Solo saltar si estamos en el suelo y no estamos stuneados/dashing/bloqueando
        if (isGrounded && characterData != null && !characterData.isStunned && !characterData.isDashing && !characterData.isBlocking)
        {
            if(rb != null) {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
                rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
                isGrounded = false;
                // animator?.SetTrigger("Jump");
                return true;
            }
        }
        return false;
    }

    // Permite a otros scripts consultar si está en el suelo
    public bool IsGrounded()
    {
        return isGrounded;
    }

    // Dibujar Gizmo para Ground Check
    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
}