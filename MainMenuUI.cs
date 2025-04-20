using UnityEngine;
using UnityEngine.SceneManagement; // Para cambiar de escena
using TMPro; // Si usas TextMeshPro para los textos

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI xpText;
    public TextMeshProUGUI weaponText;
    // Añade referencias para botones si no las conectas por Inspector

    [Header("Data")]
    public string playerCharacterID = "PlayerBruto"; // ID para guardar/cargar al jugador
    public string defaultOpponentID = "OpponentBruto1"; // Un oponente por defecto
    public WeaponData defaultPlayerWeapon; // ARRASTRA AQUÍ 'Fist.asset'

    private CharacterData loadedPlayerData; // Para guardar los datos cargados

    void Start()
    {
        if (defaultPlayerWeapon == null) {
             Debug.LogError("Asigna el WeaponData 'Fist' al campo Default Player Weapon en MainMenuUI!");
             // Desactivar botones o mostrar error
             return;
        }
        LoadAndDisplayPlayerData();
        // Asegura que exista un oponente por defecto para la primera vez
        SaveLoadSystem.LoadOrCreateDefaultCharacterData(defaultOpponentID, defaultPlayerWeapon);
    }

    void LoadAndDisplayPlayerData()
    {
        loadedPlayerData = SaveLoadSystem.LoadOrCreateDefaultCharacterData(playerCharacterID, defaultPlayerWeapon);

        if (loadedPlayerData != null)
        {
            if(playerNameText) playerNameText.text = playerCharacterID; // Muestra el ID como nombre por ahora
            if(levelText) levelText.text = $"Nivel: {loadedPlayerData.level}";
            if(xpText) xpText.text = $"XP: {loadedPlayerData.currentXP:F0} / {loadedPlayerData.xpToNextLevel:F0}";
            if(weaponText) weaponText.text = $"Arma: {loadedPlayerData.equippedWeapon?.weaponName ?? "Ninguna"}";
            // TODO: Mostrar skills, stats, etc.
        }
        else
        {
            // Mostrar error en la UI
            if(playerNameText) playerNameText.text = "ERROR";
            if(levelText) levelText.text = "Nivel: -";
            if(xpText) xpText.text = "XP: - / -";
            if(weaponText) weaponText.text = "Arma: -";
            Debug.LogError("Failed to load or create player data!");
        }
    }

    // --- Funciones para los Botones ---

    public void OnStartFightButtonClicked()
    {
        if (loadedPlayerData == null) {
            Debug.LogError("No se pueden iniciar la pelea, datos del jugador no cargados.");
            return;
        }

        Debug.Log("Iniciando pelea...");
        // Guarda los IDs de los personajes que lucharán para que la siguiente escena los lea
        PlayerPrefs.SetString("PlayerCharacterIDToLoad", playerCharacterID);
        PlayerPrefs.SetString("OpponentCharacterIDToLoad", defaultOpponentID); // Lucha contra el oponente por defecto
        PlayerPrefs.Save(); // Guarda los PlayerPrefs

        SceneManager.LoadScene("SampleScene"); // Cambia "SampleScene" por el nombre de tu escena de batalla
    }

    public void OnQuitButtonClicked()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit();
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // Cierra el modo Play en el editor
        #endif
    }

    // Podrías añadir botones para elegir oponente, ver inventario, etc. más adelante
}