using UnityEngine;
using Mirror;

public class PlayerNetworkWrapper : NetworkBehaviour
{
    [Header("Components to DISABLE on Remote Players")]
    [Tooltip("Перетащите сюда скрипты управления, камеры, аудиолистенеры и т.д., которые должны работать ТОЛЬКО у локального игрока.")]
    [SerializeField] private MonoBehaviour[] scriptsToDisable;
    [SerializeField] private GameObject[] objectsToDisable;
    [SerializeField] private Behaviour[] componentsToDisable; // Например, AudioListener, Camera

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Проверяем, является ли этот игрок локальным клиентом
        if (!isLocalPlayer)
        {
            DisableRemotePlayerElements();
        }
        else
        {
            EnableLocalPlayerElements();
        }
    }

    void DisableRemotePlayerElements()
    {
        // Отключаем указанные скрипты
        if (scriptsToDisable != null)
        {
            foreach (var script in scriptsToDisable)
            {
                if (script != null)
                    script.enabled = false;
            }
        }

        // Отключаем указанные компоненты (Камеры, AudioListener и т.д.)
        if (componentsToDisable != null)
        {
            foreach (var comp in componentsToDisable)
            {
                if (comp != null)
                    comp.enabled = false;
            }
        }

        // Выключаем дочерние объекты (например, камеру удаленного игрока или UI)
        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }
    }

    void EnableLocalPlayerElements()
    {
        // Здесь можно принудительно включить то, что нужно только локальному игроку (если было выключено по умолчанию)
        if (scriptsToDisable != null)
        {
            foreach (var script in scriptsToDisable)
            {
                if (script != null)
                    script.enabled = true;
            }
        }

        if (componentsToDisable != null)
        {
            foreach (var comp in componentsToDisable)
            {
                if (comp != null)
                    comp.enabled = true;
            }
        }

        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable)
            {
                if (obj != null)
                    obj.SetActive(true);
            }
        }
    }
}