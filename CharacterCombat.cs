// File: CharacterCombat.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic; // Para AoE
using Pathfinding; // Para AIPath

[RequireComponent(typeof(CharacterData))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(HealthSystem))] // Útil tener referencia
public class CharacterCombat : MonoBehaviour
{
    private CharacterData characterData; // Datos del personaje (stats, estado, cooldowns)
    private Rigidbody2D rb;             // Para movimiento físico (dash)
    private HealthSystem healthSystem;  // Para aplicar curaciones (si aplica)
    private AIPath aiPath;             // Para controlar el movimiento de A* (pausar/reanudar)
    private Animator animator;         // Para activar triggers de animación

    // Referencia al objetivo (solo HealthSystem para aplicar daño/efectos)
    private HealthSystem currentTargetHealth;

    // Referencias a Coroutines para poder detenerlas si es necesario (ej. stun)
    private Coroutine dashCoroutine;
    private Coroutine parryWindowCoroutine;
    private List<Coroutine> activeBuffCoroutines = new List<Coroutine>(); // Para buffs

    void Awake()
    {
        // Cachear referencias a componentes
        characterData = GetComponent<CharacterData>();
        rb = GetComponent<Rigidbody2D>();
        healthSystem = GetComponent<HealthSystem>();
        aiPath = GetComponent<AIPath>(); // Puede ser null si no se usa A*
        animator = GetComponent<Animator>(); // Puede ser null si no hay Animator
    }

    void Update()
    {
        // Drenar stamina si está bloqueando y no está stuneado
        if (characterData.isBlocking && !characterData.isStunned && characterData.baseStats != null)
        {
            // Consumir stamina por segundo
            bool stillHasStamina = characterData.ConsumeStamina(characterData.baseStats.blockStaminaDrain * Time.deltaTime);
            if (!stillHasStamina)
            {
                StopBlocking(); // Dejar de bloquear si se acaba la stamina
            }
        }
    }

    // Establece el objetivo actual para las acciones de combate
    public void SetTarget(HealthSystem newTarget)
    {
        currentTargetHealth = newTarget;
    }
    // Obtiene la referencia al HealthSystem del objetivo actual
    public HealthSystem GetTarget()
    {
        return currentTargetHealth;
    }

    // Detiene todas las acciones de combate activas (llamado por Stun, etc.)
    public void InterruptActions()
    {
        // Detener Dash si está activo
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);
            rb.linearVelocity = Vector2.zero; // Detener movimiento físico
            characterData.SetDashing(false);
            characterData.SetInvulnerable(false); // Quitar invulnerabilidad si la tenía
            dashCoroutine = null;
        }

        // Detener ventana de Parry si está activa
        if (parryWindowCoroutine != null)
        {
            StopCoroutine(parryWindowCoroutine);
            characterData.SetAttemptingParry(false);
            parryWindowCoroutine = null;
        }

        // Detener Bloqueo si estaba activo
        StopBlocking();

        // Detener otras corutinas de acción (ej. casteo/delay de skills, daño pendiente)
        StopCoroutine(nameof(ExecuteSkillCoroutine));
        StopCoroutine(nameof(ApplyAttackDamageAfterDelay));

        // Opcional: Resetear triggers del animator si causan problemas al interrumpir
        // if (animator != null) { ... }
    }

    // Intenta realizar un ataque básico
    public bool TryAttack()
    {
        // Validaciones: No atacar si está stuneado, dasheando, bloqueando, sin stats, ataque en cooldown, sin objetivo vivo o fuera de rango
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsAttackReady()) return false;
        if (currentTargetHealth == null || !currentTargetHealth.IsAlive()) return false;
        if (!IsTargetInBasicAttackRange(currentTargetHealth.transform)) return false;

        characterData.PutAttackOnCooldown(); // Poner el ataque en cooldown
        Debug.Log($"{gameObject.name} ataca a {currentTargetHealth.gameObject.name}");

        // Activar animación de ataque
        if (animator != null) animator.SetTrigger("Attack");
        else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Attack'");

        // Aplicar daño después de un pequeño delay (simula el punto de impacto de la animación)
        StartCoroutine(ApplyAttackDamageAfterDelay(currentTargetHealth, 0.2f)); // Ajustar delay según animación
        // Pausar movimiento A* durante la animación de ataque (opcional)
        if (aiPath != null) StartCoroutine(PauseMovementDuringAction(0.5f)); // Ajustar duración según animación

        return true; // Ataque iniciado
    }

    // Corutina para aplicar el daño del ataque básico después de un delay
    IEnumerator ApplyAttackDamageAfterDelay(HealthSystem target, float delay)
    {
        yield return new WaitForSeconds(delay);
        // Volver a comprobar condiciones antes de aplicar daño (puede haber cambiado el estado)
        if (!characterData.isStunned && target != null && target.IsAlive() && IsTargetInBasicAttackRange(target.transform) && characterData.baseStats != null)
        {
            target.TakeDamage(characterData.baseStats.attackDamage, this.gameObject); // Aplicar daño
        }
    }

    // Intenta realizar un dash en la dirección especificada
    public bool TryDash(Vector2 direction)
    {
        // Validaciones: No dashear si está stuneado, ya dasheando, bloqueando, sin stats, dash en cooldown o sin stamina
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsDashReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.dashCost)) return false;

        characterData.PutDashOnCooldown(); // Poner dash en cooldown
        InterruptActions(); // Interrumpir otras acciones antes de empezar el dash

        if (dashCoroutine != null) StopCoroutine(dashCoroutine); // Seguridad extra
        dashCoroutine = StartCoroutine(DashCoroutine(direction.normalized)); // Iniciar corutina de dash

        // Activar animación de dash
        if (animator != null) animator.SetTrigger("Dash");
        else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Dash'");
        return true; // Dash iniciado
    }

    // Corutina que maneja la lógica del dash (movimiento, invulnerabilidad, duración)
    IEnumerator DashCoroutine(Vector2 direction)
    {
        if (characterData.baseStats == null) yield break; // Salir si no hay stats

        characterData.SetDashing(true); // Marcar estado como dashing
        StartCoroutine(InvulnerabilityWindow(characterData.baseStats.dashInvulnerabilityDuration)); // Iniciar ventana de invulnerabilidad

        float startTime = Time.time;
        float moveSpeed = characterData.baseStats.movementSpeed;
        float dashSpeed = moveSpeed * characterData.baseStats.dashSpeedMult; // Calcular velocidad de dash
        if (aiPath != null) aiPath.canMove = false; // Pausar movimiento A*

        float originalGravity = rb.gravityScale; // Guardar gravedad original
        rb.gravityScale = 0; // Quitar gravedad durante el dash

        // Bucle principal del dash (mientras dure y no sea interrumpido)
        while (Time.time < startTime + characterData.baseStats.dashDuration)
        {
            if (characterData.isStunned) // Salir si es stuneado durante el dash
            {
                rb.linearVelocity = Vector2.zero;
                rb.gravityScale = originalGravity; // Restaurar gravedad
                characterData.SetDashing(false);
                dashCoroutine = null;
                yield break;
            }
            rb.linearVelocity = direction * dashSpeed; // Aplicar velocidad de dash
            yield return null; // Esperar al siguiente frame
        }

        // Fin del Dash normal
        rb.linearVelocity = Vector2.zero; // Detener movimiento
        rb.gravityScale = originalGravity; // Restaurar gravedad
        characterData.SetDashing(false); // Quitar estado dashing
        if (aiPath != null && !characterData.isStunned) aiPath.canMove = true; // Permitir que A* se mueva de nuevo si no está stuneado
        dashCoroutine = null; // Limpiar referencia a la corutina
        Debug.Log($"{gameObject.name} terminó dash");
    }

    // Corutina para la ventana de invulnerabilidad (ej. durante el dash)
    IEnumerator InvulnerabilityWindow(float duration)
    {
        if (characterData.baseStats == null || duration <= 0) yield break; // Salir si no hay stats o duración es 0
        characterData.SetInvulnerable(true); // Activar invulnerabilidad
        float endTime = Time.time + duration;
        while(Time.time < endTime && !characterData.isStunned) { // Esperar hasta el final o hasta ser stuneado
            yield return null;
        }
        // Solo quitar invulnerabilidad si la corutina terminó normalmente (no por stun)
        if (!characterData.isStunned) {
             characterData.SetInvulnerable(false);
        }
    }

    // Intenta empezar a bloquear
    public bool TryStartBlocking()
    {
        // Validaciones: No bloquear si stuneado, dasheando, ya bloqueando, o sin stats
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (characterData.currentStamina >= 0) // Comprobar si tiene algo de stamina (aunque sea 0) para iniciar
        {
            characterData.SetBlocking(true); // Activar estado de bloqueo
            // Reducir velocidad de A* si se usa
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed * characterData.baseStats.blockSpeedMultiplier;

            // Activar animación de bloqueo
            if (animator != null) animator.SetBool("IsBlocking", true);
            else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar bool 'IsBlocking'");
            Debug.Log($"{gameObject.name} empieza a bloquear");
            return true; // Bloqueo iniciado
        }
        return false; // No se pudo iniciar (sin stamina)
    }

    // Detiene el estado de bloqueo
    public void StopBlocking()
    {
        if (characterData.isBlocking && characterData.baseStats != null) // Solo si estaba bloqueando
        {
            characterData.SetBlocking(false); // Desactivar estado
            if (aiPath != null) aiPath.maxSpeed = characterData.baseStats.movementSpeed; // Restaurar velocidad A*

            // Desactivar animación de bloqueo
            if (animator != null) animator.SetBool("IsBlocking", false);
            Debug.Log($"{gameObject.name} deja de bloquear");
        }
    }

    // Intenta realizar un parry
    public bool TryParry()
    {
        // Validaciones: No parry si stuneado, dasheando, bloqueando, sin stats, parry en cooldown o sin stamina
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking || characterData.baseStats == null) return false;
        if (!characterData.IsParryReady()) return false;
        if (!characterData.ConsumeStamina(characterData.baseStats.parryCost)) return false;

        characterData.PutParryOnCooldown(); // Poner parry en cooldown
        InterruptActions(); // Interrumpir otras acciones

        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine); // Detener parry anterior si existiera
        parryWindowCoroutine = StartCoroutine(ParryWindowCoroutine()); // Iniciar ventana de parry

        // Activar animación de parry
        if (animator != null) animator.SetTrigger("Parry");
        else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger 'Parry'");
        Debug.Log($"{gameObject.name} intenta parry!");
        return true; // Parry iniciado
    }

    // Corutina que define la ventana de tiempo activa para el parry
    IEnumerator ParryWindowCoroutine()
    {
        if (characterData.baseStats == null) yield break; // Salir si no hay stats
        characterData.SetAttemptingParry(true); // Marcar que está intentando parry
        float endTime = Time.time + characterData.baseStats.parryWindow;
         while(Time.time < endTime && !characterData.isStunned) { // Esperar fin de ventana o stun
            yield return null;
        }

        // Usar variable local para evitar problemas si se llama TryParry de nuevo muy rápido
        bool coroutineStillActive = ReferenceEquals(parryWindowCoroutine, this.GetComponent<Coroutine>());

        // Solo desactivar flag si la ventana terminó normalmente Y esta corutina sigue siendo la activa
        if (characterData.isAttemptingParry && !characterData.isStunned && coroutineStillActive)
        {
            characterData.SetAttemptingParry(false);
        }
        // Limpiar la referencia si esta corutina específica terminó
        if (coroutineStillActive) {
           parryWindowCoroutine = null;
        }
    }

    // Llamado por HealthSystem si un ataque golpea durante la ventana de parry activa
    public void NotifySuccessfulParry(GameObject attacker)
    {
        if (!characterData.isAttemptingParry || characterData.baseStats == null) return; // Solo si estaba intentando parry

        Debug.Log($"{gameObject.name} realizó PARRY exitoso contra {attacker.name}!");

        // Detener la corutina de la ventana y el estado de intento
        if (parryWindowCoroutine != null) StopCoroutine(parryWindowCoroutine);
        characterData.SetAttemptingParry(false);
        parryWindowCoroutine = null;

        // Aplicar stun al atacante
        CharacterData attackerData = attacker.GetComponent<CharacterData>();
        if (attackerData != null)
        {
            attackerData.ApplyStun(characterData.baseStats.parryStunDuration);
        }

        // Opcional: Activar animación de parry exitoso
        // if (animator != null) animator.SetTrigger("ParrySuccess");
    }


    // Intenta usar una habilidad específica
    public bool TryUseSkill(SkillData skill)
    {
        // Validaciones básicas
        if (skill == null || characterData.baseStats == null) return false;
        if (characterData.isStunned || characterData.isDashing || characterData.isBlocking) return false;
        if (!characterData.IsSkillReady(skill)) return false; // Comprobar cooldown
        // if (!characterData.ConsumeMana(skill.manaCost)) return false; // Comprobar y consumir recurso (si aplica)

        // Comprobaciones de rango
        bool requiresTarget = skill.range > 0 || skill.skillType == SkillType.DirectDamage || skill.skillType == SkillType.Projectile; // Tipos que suelen necesitar target
        bool inRange = skill.range <= 0; // Habilidad de auto-aplicación siempre está en rango

        if (requiresTarget && currentTargetHealth != null) // Si necesita target y lo tiene
        {
            inRange = IsTargetInRange(currentTargetHealth.transform, skill.range); // Comprobar distancia
        }
        else if (requiresTarget && currentTargetHealth == null) // Si necesita target pero no hay
        {
             return false; // No se puede usar
        }

        // Permitir lanzar proyectiles/AoE aunque el target esté fuera de rango (irán en la dirección/posición)
        bool canUseOutOfRange = skill.skillType == SkillType.Projectile || skill.skillType == SkillType.AreaOfEffect;

        if (!inRange && !canUseOutOfRange) // Si no está en rango y no es un tipo que pueda usarse fuera de rango
        {
             Debug.Log($"{gameObject.name} intentó usar {skill.skillName} pero objetivo fuera de rango ({skill.range}m)");
             return false;
        }

        characterData.PutSkillOnCooldown(skill); // Poner habilidad en cooldown
        InterruptActions(); // Interrumpir otras acciones

        Debug.Log($"{gameObject.name} usa habilidad: {skill.skillName}");
        // Determinar trigger de animación (usar nombre específico o uno genérico)
        string trigger = !string.IsNullOrEmpty(skill.animationTriggerName) ? skill.animationTriggerName : "UseSkill";

        // Activar animación
         if (animator != null) animator.SetTrigger(trigger);
         else Debug.LogWarning($"Animator no encontrado en {gameObject.name}, no se puede activar trigger '{trigger}'");

        StartCoroutine(ExecuteSkillCoroutine(skill)); // Iniciar ejecución de la habilidad
        // Pausar movimiento durante la habilidad (opcional)
        if (aiPath != null) StartCoroutine(PauseMovementDuringAction(0.8f)); // Ajustar duración

        return true; // Habilidad iniciada
    }

    // Corutina que ejecuta la lógica específica de la habilidad
    IEnumerator ExecuteSkillCoroutine(SkillData skill)
    {
        if (skill == null || characterData.baseStats == null) yield break;

        // Efectos visuales/sonido de lanzamiento
        if (skill.castVFX != null) Instantiate(skill.castVFX, transform.position, transform.rotation);
        // Play castSFX

        // yield return new WaitForSeconds(skill.castTime); // Añadir si hay tiempo de casteo

        if (characterData.isStunned) yield break; // Salir si es stuneado durante ejecución

        // Lógica según el tipo de habilidad
        switch (skill.skillType)
        {
            case SkillType.DirectDamage:
                if (currentTargetHealth != null && IsTargetInRange(currentTargetHealth.transform, skill.range))
                {
                    Debug.Log($"Habilidad golpea a {currentTargetHealth.name} por {skill.damage} daño.");
                    currentTargetHealth.TakeDamage(skill.damage, this.gameObject); // Aplicar daño directo
                    if (skill.hitVFX != null) Instantiate(skill.hitVFX, currentTargetHealth.transform.position, Quaternion.identity);
                    // Play hitSFX
                }
                break;

            case SkillType.Projectile:
                 if (skill.projectilePrefab != null) {
                    Vector3 spawnPos = transform.position; // Ajustar punto de spawn si es necesario
                    // Calcular dirección: hacia el target o hacia adelante si no hay target
                    Vector3 targetPos = currentTargetHealth?.transform.position ?? (spawnPos + transform.right * skill.range);
                    Vector2 direction = ((Vector2)targetPos - (Vector2)spawnPos).normalized;
                    if (direction == Vector2.zero) direction = transform.right; // Dirección por defecto si target está encima
                    spawnPos += (Vector3)direction * 0.5f; // Avanzar un poco para evitar colisión inmediata

                    // Calcular rotación para que mire en la dirección correcta
                    float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.forward);

                    // Instanciar y configurar proyectil
                    GameObject projGO = Instantiate(skill.projectilePrefab, spawnPos, rotation);
                    Projectile projectileScript = projGO.GetComponent<Projectile>();
                    if (projectileScript != null) {
                        projectileScript.Initialize(skill.damage, skill.projectileSpeed, skill.projectileLifetime, this.gameObject, skill.hitVFX);
                    } else {
                        Debug.LogWarning($"Prefab de proyectil {skill.projectilePrefab.name} no tiene script Projectile.");
                        Destroy(projGO, skill.projectileLifetime); // Destruir igual después de un tiempo
                    }
                }
                 break;

            case SkillType.SelfBuff:
                Coroutine buffCoroutine = StartCoroutine(ApplyBuffCoroutine(skill)); // Iniciar corutina del buff
                activeBuffCoroutines.Add(buffCoroutine); // Guardar referencia para posible interrupción
                Debug.Log($"Aplicando buff {skill.skillName} por {skill.duration}s");
                break;

            case SkillType.AreaOfEffect:
                ApplyAoEEffect(skill); // Aplicar efecto en área
                break;

            case SkillType.Heal:
                 if(healthSystem != null) healthSystem.RestoreHealth(skill.healAmount); // Curar al propio personaje
                 Debug.Log($"{gameObject.name} se cura {skill.healAmount} puntos.");
                 if (skill.hitVFX != null) Instantiate(skill.hitVFX, transform.position, Quaternion.identity);
                 // Play heal SFX
                 break;
            // Añadir casos para Debuff, etc.
        }
    }

    // Aplica el efecto de área de una habilidad
     void ApplyAoEEffect(SkillData skill) {
         if (skill == null || characterData.baseStats == null) return;
         Vector2 centerPoint = transform.position; // Centro del AoE (puede ajustarse)
         // Detectar todos los colliders en el radio
         Collider2D[] hits = Physics2D.OverlapCircleAll(centerPoint, skill.aoeRadius);
         Debug.Log($"Skill AoE {skill.skillName} detectó {hits.Length} colliders en radio {skill.aoeRadius}.");

         if (skill.hitVFX != null) Instantiate(skill.hitVFX, centerPoint, Quaternion.identity);
         // Play AoE SFX

         // Determinar el tag del enemigo (basado en la IA si existe)
         string enemyTag = characterData.GetComponent<LuchadorAIController>()?.enemyTag ?? "Enemy";

         foreach (Collider2D hit in hits) {
             GameObject targetGO = hit.gameObject;
             bool isSelf = (targetGO == this.gameObject);
             bool isEnemy = targetGO.CompareTag(enemyTag);
             // Podrías añadir lógica para aliados aquí si tuvieras un sistema de equipos más complejo

             // Filtrar según configuración de la habilidad
             if (isSelf && !skill.affectsSelf) continue;
             if (isEnemy && !skill.affectsEnemies) continue;
             // if (!isSelf && !isEnemy && !skill.affectsAllies) continue; // Si hubiera aliados

             // Aplicar efectos al objetivo válido
             HealthSystem targetHealth = hit.GetComponent<HealthSystem>();
             if (targetHealth != null && targetHealth.IsAlive())
             {
                 // Aplicar daño si corresponde
                 if (skill.damage > 0 && (isEnemy || (isSelf && skill.affectsSelf))) { // Dañar enemigos o a sí mismo si está configurado
                     targetHealth.TakeDamage(skill.damage, this.gameObject);
                     Debug.Log($"AoE golpea a {targetGO.name} por {skill.damage} daño.");
                 }
                 // Aplicar curación si corresponde
                 if (skill.healAmount > 0 && (isSelf || (isEnemy && skill.affectsEnemies))) { // Curar a sí mismo o enemigos (raro pero posible)
                      targetHealth.RestoreHealth(skill.healAmount);
                     Debug.Log($"AoE cura a {targetGO.name} por {skill.healAmount}.");
                 }
             }
             // Aplicar otros efectos (Stun, Debuff) al CharacterData si existe
             CharacterData targetData = hit.GetComponent<CharacterData>();
              if(targetData != null && !targetData.isStunned) {
                 // if(skill.stunDuration > 0) { targetData.ApplyStun(skill.stunDuration); }
                 // Aplicar otros debuffs...
             }
         }
     }

    // Corutina para aplicar un buff temporal
    IEnumerator ApplyBuffCoroutine(SkillData buffSkill)
    {
        if (buffSkill == null || characterData.baseStats == null) yield break;
        // Guardar valores originales de los stats que se van a modificar
        float originalAiPathSpeed = aiPath != null ? aiPath.maxSpeed : 0;
        // Añadir variables para otros stats originales aquí...
        bool applied = false; // Flag para saber si se aplicó algún cambio

        try // Usar try-finally para asegurar que se reviertan los cambios
        {
            // Aplicar el buff según el stat especificado
            switch (buffSkill.buffStat)
            {
                case StatToBuff.Speed:
                    if (aiPath != null) { aiPath.maxSpeed *= buffSkill.buffMultiplier; applied = true; }
                    break;
                case StatToBuff.Damage:
                    // Necesitarías modificar CharacterStats temporalmente o tener un multiplicador en CharacterData
                    // Ejemplo: characterData.damageMultiplier *= buffSkill.buffMultiplier; applied = true;
                    break;
                // Casos para otros buffs (Defense, AttackSpeed, etc.)
            }

            // Esperar la duración del buff si se aplicó algo
            if (applied)
            {
                 float endTime = Time.time + buffSkill.duration;
                 while(Time.time < endTime && !characterData.isStunned) { // Terminar si se acaba el tiempo o es stuneado
                    yield return null;
                 }
                 Debug.Log($"Buff {buffSkill.skillName} terminó (Duración o Stun).");
            }
        }
        finally // Este bloque se ejecuta siempre, incluso si la corutina se detiene antes
        {
             // Revertir los cambios si se aplicaron
             if (applied) {
                 switch (buffSkill.buffStat)
                 {
                    case StatToBuff.Speed:
                        if (aiPath != null) aiPath.maxSpeed = originalAiPathSpeed; // Revertir velocidad A*
                        break;
                    case StatToBuff.Damage:
                        // Revertir multiplicador de daño
                        // Ejemplo: characterData.damageMultiplier /= buffSkill.buffMultiplier;
                        break;
                    // Revertir otros buffs
                 }
                 Debug.Log($"Buff {buffSkill.skillName} revertido.");
             }
             // Eliminar esta corutina de la lista de activas
             activeBuffCoroutines.RemoveAll(c => c == this.GetComponent<Coroutine>()); // Intento simple de encontrarla
        }
    }

    // --- HELPERS ---

    // Comprueba si el target está dentro del rango de ataque básico
    private bool IsTargetInBasicAttackRange(Transform targetTransform)
    {
        if (characterData.baseStats == null) return false;
        return IsTargetInRange(targetTransform, characterData.baseStats.attackRange);
    }

    // Comprueba si un transform está dentro de un rango específico (usa sqrMagnitude para eficiencia)
    public bool IsTargetInRange(Transform targetTransform, float range)
    {
        if (targetTransform == null) return false;
        return (targetTransform.position - transform.position).sqrMagnitude <= range * range;
    }

    // Corutina para pausar el movimiento de A* durante una acción
    IEnumerator PauseMovementDuringAction(float duration)
    {
        if (aiPath != null && aiPath.canMove) // Solo si A* existe y se estaba moviendo
        {
            aiPath.canMove = false; // Pausar movimiento
             float endTime = Time.time + duration;
             while(Time.time < endTime && !characterData.isStunned) { // Esperar duración o stun
                yield return null;
             }

            // Al terminar, NO reanudar automáticamente el movimiento.
            // La IA (MakeDecision) decidirá si debe volver a moverse en el siguiente ciclo.
            // if (!characterData.isStunned ... ) { aiPath.canMove = true; } // Evitar esto
        }
    }
}