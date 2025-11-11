using UnityEngine;
using System; // Para Exception y DateTime
using System.IO; // Para FileStream y manejo de archivos
using System.Runtime.InteropServices; // ¡Para llamar a código nativo (P/Invoke)!

/// <summary>
/// Gestor principal del SDK de Crash Reporting.
/// Se encarga de interceptar excepciones no controladas y 
/// de disparar la recolección de dumps de memoria.
/// 
/// Creado por Yulieta Eyzaguirre (con ayuda de Gemini)
/// </summary>
public class CrashReporterManager : MonoBehaviour
{
    private static CrashReporterManager _instance;
    private string _logPath;

    #region Ciclo de Vida de Unity y Singleton

    private void Awake()
    {
        // Implementación de Singleton para que persista entre escenas
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Define dónde se guardarán los reportes. 
            // Application.persistentDataPath es una carpeta segura en cada plataforma.
            _logPath = Path.Combine(Application.persistentDataPath, "CrashReports");

            // Asegúrate de que el directorio exista
            Directory.CreateDirectory(_logPath);

            Debug.Log($"[CrashReporter] Iniciado. Los reportes se guardarán en: {_logPath}");
        }
        else
        {
            // Si ya existe una instancia, destruye este duplicado.
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        // (1) ESTA ES LA INTEGRACIÓN PRINCIPAL.
        // Nos suscribimos a TODOS los mensajes de log de Unity.
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        // Buena práctica: desuscribirse al ser deshabilitado.
        Application.logMessageReceived -= HandleLog;
    }

    #endregion

    /// <summary>
    /// (2) OPTIMIZACIÓN DEL MANEJO DE EXCEPCIONES
    /// Este método se llama cada vez que Unity registra un log, warning, error o excepción.
    /// </summary>
    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        // Solo nos interesan las excepciones (¡los "crashes" de script!)
        if (type == LogType.Exception)
        {
            Debug.LogWarning("[CrashReporter] ¡Excepción de Unity detectada!");

            // --- 1. Formatear el reporte de error de texto ---
            string reportFileName = $"Crash_Report_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
            string reportFilePath = Path.Combine(_logPath, reportFileName);

            string fullReport = "--- INICIO REPORTE DE CRASH (SDK Yulieta E.) ---\n\n";
            fullReport += $"Fecha y Hora: {DateTime.Now}\n";
            fullReport += $"Plataforma: {Application.platform}\n";
            fullReport += $"Versión de Unity: {Application.unityVersion}\n\n";
            fullReport += "--- ERROR ---\n";
            fullReport += logString + "\n\n";
            fullReport += "--- STACK TRACE ---\n";
            fullReport += stackTrace;
            fullReport += "\n--- FIN REPORTE DE CRASH ---";

            // --- 2. Guardar el reporte de texto en el disco ---
            try
            {
                File.WriteAllText(reportFilePath, fullReport);
                Debug.Log($"[CrashReporter] Reporte de texto guardado en: {reportFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CrashReporter] No se pudo guardar el reporte de texto: {e.Message}");
            }

            // --- 3. Disparar la recolección del dump de memoria ---
            // Esto es específico de la plataforma (ver abajo)
            // Usamos directivas de precompilador para que solo intente esto en Windows.
#if UNITY_STANDALONE_WIN
            CreateMiniDump();
#endif
        }
    }

    #region (3) RECOLECCIÓN DE DUMPS DE MEMORIA (Específico de Windows)

    // Esta es la parte avanzada que usa P/Invoke (Platform Invocation Services)
    // para llamar a código nativo de Windows (DbgHelp.dll)

    // --- Definiciones de P/Invoke para MiniDumpWriteDump ---

    // Banderas para definir qué incluir en el dump
    [Flags]
    public enum MiniDumpType
    {
        Normal = 0x00000000,
        WithDataSegs = 0x00000001,
        WithFullMemory = 0x00000002,
        WithHandleData = 0x00000004,
        WithProcessThreadData = 0x00000010,
        WithFullMemoryInfo = 0x00000800,
        WithThreadInfo = 0x00001000,
        WithCodeSegs = 0x00002000,
        // ... hay muchas más banderas, pero estas son las comunes
    }

    // El "Import" de la DLL nativa de Windows que hace el trabajo
    [DllImport("DbgHelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        SafeHandle hFile, // Un "handle" seguro al archivo
        MiniDumpType dumpType,
        IntPtr expParam,
        IntPtr userStreamParam,
        IntPtr callbackParam
    );

    /// <summary>
    /// Crea un archivo "minidump" del proceso actual.
    /// ESTO SOLO FUNCIONA EN UNA BUILD DE WINDOWS O EN EL EDITOR DE WINDOWS.
    /// </summary>
    private void CreateMiniDump()
    {
        // Esta directiva asegura que este código ni siquiera exista en otras plataformas
#if UNITY_STANDALONE_WIN

        string dumpFileName = $"Crash_Dump_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.dmp";
        string dumpFilePath = Path.Combine(_logPath, dumpFileName);

        try
        {
            // Usamos un FileStream para obtener el "Handle" del archivo
            using (var fs = new FileStream(dumpFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                // Obtenemos el proceso actual de Unity
                var process = System.Diagnostics.Process.GetCurrentProcess();
                IntPtr processHandle = process.Handle;
                uint processId = (uint)process.Id;

                // Definimos qué queremos en el dump (un dump pequeño con hilos y datos)
                MiniDumpType dumpType = MiniDumpType.WithProcessThreadData | MiniDumpType.WithHandleData | MiniDumpType.WithThreadInfo;

                // ¡Llamada a la función nativa de Windows!
                bool success = MiniDumpWriteDump(
                    processHandle,
                    processId,
                    fs.SafeFileHandle, // El handle del archivo donde se escribirá
                    dumpType,
                    IntPtr.Zero, // Sin info de excepción específica
                    IntPtr.Zero, // Sin stream de usuario
                    IntPtr.Zero  // Sin callback
                );

                if (success)
                {
                    Debug.Log($"[CrashReporter] ¡Dump de memoria guardado exitosamente en: {dumpFilePath}!");
                }
                else
                {
                    // Obtener el error nativo si falla
                    int lastError = Marshal.GetLastWin32Error();
                    Debug.LogError($"[CrashReporter] No se pudo crear el minidump. Error nativo (Win32): {lastError}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[CrashReporter] Excepción al intentar crear el minidump: {e.Message}");
        }

#endif
    }

    #endregion

    #region Herramienta de Prueba

    // Función de ayuda para probar el SDK fácilmente
    // Muestra un botón en la pantalla del juego
    private void OnGUI()
    {
        // Botón 1: Forzar Crash
        if (GUI.Button(new Rect(10, 10, 250, 60), "Forzar CRASH de prueba (SDK)"))
        {
            Debug.LogWarning("¡Forzando un crash de prueba!");

            // Forzamos una excepción que será capturada por nuestro HandleLog
            GameObject obj = null;
            obj.name = "Esto causará una NullReferenceException"; // ¡CRASH!
        }

        // Botón 2: Abrir carpeta de reportes
        // Usamos Application.OpenURL(), que funciona para abrir carpetas en el explorador de archivos.
        // Lo ponemos 70 píxeles más abajo que el primer botón (10 + 60 + 10 de espacio)
        if (GUI.Button(new Rect(10, 80, 250, 40), "Abrir Carpeta de Reportes"))
        {
            // Primero, comprobamos que exista (se crea en Awake)
            if (Directory.Exists(_logPath))
            {
                Application.OpenURL(_logPath);
            }
            else
            {
                Debug.LogError("[CrashReporter] La carpeta de reportes aún no existe. ¡Provoca un crash primero!");
            }
        }
    }

    #endregion
}