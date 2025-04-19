using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterData))]
public class CharacterMovement : MonoBehaviour
{
    private Rigidbody2D rb;
    private CharacterData characterData;
    private Transform target;
    private bool canMove = true;

    // --- NUEVAS VARIABLES PARA SALTO Y GROUND CHECK ---
    [Header("Jumping & Ground Check")]
    public float jumpForce = 10f;          // Fuerza del salto (ajustar en Inspector)
    public Transform groundCheckPoint;    // Un GameObject vacío hijo, posicionado a los pies del personaje
    public float groundCheckRadius = 0.2f; // Radio del círculo para detectar suelo (ajustar)
    public LayerMask groundLayer;         // Qué capas se consideran suelo (asignar "Ground" en Inspector)

    private bool isGrounded;
    // -------------------------------------------------


    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        characterData = GetComponent<CharacterData>();

        // --- VERIFICACIÓN DEL GROUND CHECK POINT ---
        if (groundCheckPoint == null)
        {
            // Intentar encontrarlo si se llama "GroundCheck", si no, crearlo
            Transform foundGroundCheck = transform.Find("GroundCheck");
            if (foundGroundCheck != null)
            {
                groundCheckPoint = foundGroundCheck;
            }
            else
            {
                // Crear un GameObject hijo vacío para el ground check
                GameObject groundCheckObj = new GameObject("GroundCheck");
                groundCheckObj.transform.SetParent(transform);
                // Posicionarlo ligeramente por debajo del centro (ajustar según el sprite)
                groundCheckObj.transform.localPosition = new Vector3(0, -0.5f, 0);
                groundCheckPoint = groundCheckObj.transform;
                Debug.LogWarning("GroundCheckPoint no asignado en " + gameObject.name + ". Se creó uno automáticamente. Ajusta su posición si es necesario.", this);
            }
        }
        // -------------------------------------------
    }

    void Update() // Ground check es mejor hacerlo en Update o FixedUpdate
    {
        CheckIfGrounded();
    }

    void FixedUpdate()
    {
        // --- MOVIMIENTO HORIZONTAL ---
        // Aplicar movimiento horizontal si podemos y tenemos objetivo
        if (target != null && canMove)
        {
            float horizontalDirection = Mathf.Sign(target.position.x - transform.position.x);
            // Mantenemos la velocidad vertical actual (gravedad/salto)
            // y solo modificamos la horizontal.
            rb.linearVelocity = new Vector2(horizontalDirection * characterData.baseStats.movementSpeed, rb.linearVelocity.y);
        }
        // Si no debemos movernos horizontalmente (sin target o canMove=false)
        // pero sí queremos que la gravedad actúe.
        else if (!canMove || target == null)
        {
             rb.linearVelocity = new Vector2(0, rb.linearVelocity.y); // Detener movimiento horizontal, mantener vertical
        }
        // -----------------------------
    }

     // --- NUEVA FUNCIÓN: CHECK IF GROUNDED ---
    private void CheckIfGrounded()
    {
        // Dibuja un círculo en la posición de groundCheckPoint y comprueba si colisiona con groundLayer
        isGrounded = Physics2D.OverlapCircle(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }
    // ----------------------------------------

    // --- NUEVA FUNCIÓN: JUMP ---
    public bool Jump()
    {
        // Solo saltar si estamos en el suelo y podemos movernos
        if (isGrounded && canMove)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // Opcional: Resetea velocidad vertical antes de saltar
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            // isGrounded se volverá falso automáticamente en el próximo CheckIfGrounded()
            return true; // Salto realizado
        }
        return false; // No se pudo saltar (en el aire o movimiento bloqueado)
    }
    // ---------------------------

    // --- NUEVA FUNCIÓN PÚBLICA: IS GROUNDED ---
    // Para que la IA pueda consultarlo
    public bool IsGrounded()
    {
        return isGrounded;
    }
    // ----------------------------------------

    // --- MÉTODOS EXISTENTES (SetTarget, StopMovement, ResumeMovement, GetDistanceToTarget) ---
     public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public Transform GetTarget()
    {
        return target;
    }

    public void StopMovement() // Ahora solo detiene la capacidad de iniciar movimiento/salto
    {
         canMove = false;
         rb.linearVelocity = new Vector2(0, rb.linearVelocity.y); // Detener solo horizontal
    }

     public void ResumeMovement()
    {
        canMove = true;
    }

    public float GetDistanceToTarget()
    {
        if (target == null) return float.MaxValue;
        // Considerar solo la distancia horizontal o la distancia total? Por ahora, total.
        return Vector2.Distance(transform.position, target.position);
    }
    // ---------------------------------------------------------------------------------------


    // --- OPCIONAL: DIBUJAR GIZMO PARA GROUND CHECK ---
    void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
    // -----------------------------------------------
}