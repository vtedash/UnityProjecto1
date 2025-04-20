using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    private float damage;
    private float speed;
    private GameObject owner;
    private GameObject hitVFXPrefab;
    private Rigidbody2D rb;
    private bool hitOccurred = false;

    public void Initialize(float dmg, float spd, float lifetime, GameObject projOwner, GameObject vfx)
    {
        damage = dmg;
        speed = spd;
        owner = projOwner;
        hitVFXPrefab = vfx;
        rb = GetComponent<Rigidbody2D>();

        GetComponent<Collider2D>().isTrigger = true;
        rb.gravityScale = 0;
        // *** CORRECCIÓN OBSOLETO ***
        rb.bodyType = RigidbodyType2D.Dynamic; // Usar bodyType en lugar de isKinematic

        rb.linearVelocity = transform.right * speed;
        Destroy(gameObject, lifetime);
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (hitOccurred || collision.gameObject == owner) return;

        HealthSystem targetHealth = collision.GetComponent<HealthSystem>();
        if (targetHealth != null)
        {
            hitOccurred = true;
            // Debug.Log($"Projectile hit {collision.name} for {damage} damage"); // Comentado log
            targetHealth.TakeDamage(damage, owner);
            if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position, Quaternion.identity);
            Destroy(gameObject); // Destruir al impactar un objetivo con vida
        }
        else if (!collision.isTrigger) // Choca con algo sólido que no es trigger
        {
             hitOccurred = true;
             // Debug.Log($"Projectile hit obstacle {collision.name}"); // Comentado log
             if (hitVFXPrefab != null) Instantiate(hitVFXPrefab, transform.position, Quaternion.identity);
             Destroy(gameObject); // Destruir al chocar con obstáculos
        }
        // Si choca con otro trigger que no tiene HealthSystem, lo ignora y sigue
    }
}