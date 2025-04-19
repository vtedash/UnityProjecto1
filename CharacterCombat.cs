using UnityEngine;
using System.Collections; // Necesario para Coroutines

[RequireComponent(typeof(CharacterData))]
public class CharacterCombat : MonoBehaviour
{
    private CharacterData characterData;
    private HealthSystem targetHealth; // La salud del objetivo actual
    private float lastAttackTime;
    private bool canAttack = true;

    void Awake()
    {
        characterData = GetComponent<CharacterData>();
    }

    void Update()
    {
         // No hacemos nada si no podemos atacar
         if (!canAttack) return;

         // Lógica de ataque (simplificada, se moverá a la IA)
         // Esto es solo para probar el ataque. La decisión vendrá de la IA.
         // if (targetHealth != null && Time.time >= lastAttackTime + characterData.baseStats.attackCooldown)
         // {
         //     // Verificar rango aquí antes de llamar a Attack()
         //     // float distance = Vector2.Distance(transform.position, targetHealth.transform.position);
         //     // if (distance <= characterData.baseStats.attackRange) {
         //     //     Attack(targetHealth);
         //     // }
         // }
    }

    public void SetTarget(HealthSystem newTarget)
    {
         targetHealth = newTarget;
    }

    public void Attack(HealthSystem targetToAttack)
    {
        if (targetToAttack == null || !targetToAttack.IsAlive() || !canAttack) return; // No atacar si no hay objetivo, está muerto o no podemos

        if (Time.time >= lastAttackTime + characterData.baseStats.attackCooldown)
        {
            Debug.Log(gameObject.name + " ataca a " + targetToAttack.gameObject.name);
            targetToAttack.TakeDamage(characterData.baseStats.attackDamage);
            lastAttackTime = Time.time;

            // Podrías añadir un pequeño cooldown visual o efecto aquí
        }
    }

    public bool IsTargetInRange(Transform targetTransform)
    {
         if (targetTransform == null) return false;
         float distance = Vector2.Distance(transform.position, targetTransform.position);
         return distance <= characterData.baseStats.attackRange;
    }

    public void EnableAttack()
    {
        canAttack = true;
    }

    public void DisableAttack()
    {
        canAttack = false;
    }
}