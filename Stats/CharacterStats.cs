using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterStats", menuName = "MyGame/Character Stats")]
public class CharacterStats : ScriptableObject
{
    [Header("Core Stats")]
    public float maxHealth = 100f;
    public float movementSpeed = 3f;
    public float attackRange = 1f;
    public float attackDamage = 10f;
    public float attackCooldown = 1f; // Tiempo entre ataques
    // Añadir más stats aquí (Agilidad, Fuerza, etc.) si se usan directamente pronto
}