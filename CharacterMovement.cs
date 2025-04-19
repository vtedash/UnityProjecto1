using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CharacterData))]
public class CharacterMovement : MonoBehaviour
{
    private Rigidbody2D rb;
    private CharacterData characterData;
    private Transform target; // El objetivo a seguir
    private bool canMove = true;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        characterData = GetComponent<CharacterData>();
    }

    void FixedUpdate()
    {
        if (target != null && canMove)
        {
            // Solo nos movemos si tenemos un objetivo y podemos movernos
            Vector2 direction = ((Vector2)target.position - rb.position).normalized;
            rb.linearVelocity = direction * characterData.baseStats.movementSpeed; // Usamos velocidad directa para control simple
        }
        else
        {
            rb.linearVelocity = Vector2.zero; // Detenerse si no hay objetivo o no se puede mover
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public Transform GetTarget()
    {
        return target;
    }

    public void StopMovement()
    {
         canMove = false;
         rb.linearVelocity = Vector2.zero;
    }

     public void ResumeMovement()
    {
        canMove = true;
    }

    // Helper para saber la distancia al objetivo
    public float GetDistanceToTarget()
    {
        if (target == null) return float.MaxValue;
        return Vector2.Distance(rb.position, target.position);
    }
}