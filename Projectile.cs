using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))] // Asegura que tenga un collider para detectar colisiones
public class Projectile : MonoBehaviour
{
    private float damage;
    private float speed;
    private GameObject owner; // Quién disparó esto
    private GameObject hitVFXPrefab;
    private Rigidbody2D rb;
    private bool hitOccurred = false; // Para evitar doble impacto

    // Llama a esto justo después de Instanciar el proyectil
    public void Initialize(float dmg, float spd, float lifetime, GameObject projOwner, GameObject vfx)
    {
        damage = dmg;
        speed = spd;
        owner = projOwner;
        hitVFXPrefab = vfx;
        rb = GetComponent<Rigidbody2D>();

        // Asegurar que el collider sea Trigger si no lo es
        GetComponent<Collider2D>().isTrigger = true;
        // Asegurar que el Rigidbody2D no use gravedad y esté configurado para movimiento kinematico o vía velocity
        rb.gravityScale = 0;
        rb.isKinematic = false; // O true si prefieres moverlo con rb.MovePosition

        // Aplicar velocidad inicial - Asume que el proyectil "mira" hacia la derecha (transform.right)
        // La rotación se debe establecer correctamente durante la instanciación en CharacterCombat
        rb.linearVelocity = transform.right * speed;

        // Autodestrucción después de un tiempo
        Destroy(gameObject, lifetime);
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        // Si ya golpeó algo, no hacer nada más
        if (hitOccurred) return;

        // Ignorar colisión con el dueño del proyectil
        if (collision.gameObject == owner) return;

        // Opcional: Ignorar colisión con otros proyectiles (si están en un layer específico)
        // if (collision.gameObject.CompareTag("Projectile")) return;

        // Opcional: Ignorar colisión con triggers que no sean personajes/obstáculos
        // if (collision.isTrigger && collision.GetComponent<HealthSystem>() == null) return;


        HealthSystem targetHealth = collision.GetComponent<HealthSystem>();
        // Comprobar si es un objetivo válido (tiene HealthSystem y no es el dueño)
        if (targetHealth != null)
        {
            hitOccurred = true; // Marcar que ya golpeó
            Debug.Log($"Proyectil de {owner?.name ?? "Unknown"} golpea a {collision.name} por {damage} daño");
            targetHealth.TakeDamage(damage, owner); // Pasar el dueño como atacante

            // Instanciar efecto de impacto si existe
            if (hitVFXPrefab != null)
            {
                Instantiate(hitVFXPrefab, transform.position, Quaternion.identity);
            }

            // Detener y destruir el proyectil
            rb.linearVelocity = Vector2.zero; // Detener movimiento
            Destroy(gameObject);
        }
        else
        {
            // Opcional: Destruir al chocar con obstáculos sólidos si no es un trigger
             if (!collision.isTrigger /* && collision.gameObject.layer == LayerMask.NameToLayer("Obstacles") */ )
             {
                  hitOccurred = true;
                 Debug.Log($"Proyectil choca contra {collision.name}");
                 if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position, Quaternion.identity);
                 rb.linearVelocity = Vector2.zero;
                 Destroy(gameObject);
             }
        }
    }
}