using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Xml.Linq;
using IWshRuntimeLibrary;
using Microsoft.Win32;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using System.Security.AccessControl;
using System.Security.Principal;

using SharpCompress.Archives;
using SharpCompress.Readers;
using System.IO;


Console.WriteLine("Hello, World!");

async Task Main(string[] args)
{
    AddToStartup();
    string baseUrl = "http://localhost:8081";
    //string baseUrl = "http://202.180.218.84/";
    string appFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AppFolder");
    try {
        String uniqueId;
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography"))
        {
            uniqueId = key.GetValue("MachineGuid").ToString();
        }
        string manifestFilePath = FindApplicationFile(appFolderPath);
        string clickOnceApplicationVersion = GetClickOnceApplicationVersion(manifestFilePath);
        var client = new HttpClient();
        string token = await GetTokenTo(client, baseUrl, uniqueId);
        bool shouldUpdate = await ShouldUpdateAsync(client, baseUrl, clickOnceApplicationVersion, uniqueId);

        //string latestVersion = await GetLatestVersionAsync(client, baseUrl);

        if (shouldUpdate)
        {
            string zipFilePath = await DownloadAppAsync(client, baseUrl);
            if (zipFilePath != null)
            {
                DecompressApp(zipFilePath, appFolderPath);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error" + ex.ToString());
    }
    
    RunApplicationManifest(appFolderPath);
}


async Task<String> GetTokenTo(HttpClient client, String baseUrl, String uniqueId)
{
    String responseString = "";
    var waitTime = 0;

    var obj = new
    {
        code = uniqueId
    };

    var json = JsonConvert.SerializeObject(obj);
    var content = new StringContent(json, Encoding.UTF8, "application/json");
    var response = await client.PostAsync($"{baseUrl}/manage-api/v1/recordApi/login", content);
    //if response is success then get token if not then log error and try again after 1 plus minute from last try
    if (response.IsSuccessStatusCode)
    {
        responseString = await response.Content.ReadAsStringAsync();
    }
    else
    {
        waitTime = waitTime + 1;
        await Task.Delay(waitTime * 60000);
        //System.Threading.Thread.Sleep(waitTime * 60000);
        await GetTokenTo(client, baseUrl, uniqueId);

    }
    client.DefaultRequestHeaders.Add("x-auth-token", responseString);
    return responseString;
    
}

static async Task<string> GetLatestVersionAsync(HttpClient client, string baseUrl)
{
    try
    {
        string url = baseUrl + "/manage-api/v1/version/name";
        HttpResponseMessage response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode)
        {
            string version = await response.Content.ReadAsStringAsync();
            return version;
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode}");
            return null;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Error: {e.Message}");
        return null;
    }
}


static async Task<bool> ShouldUpdateAsync(HttpClient client, string baseUrl, string version, string deviceCode)
{
    try
    {
        string url = baseUrl + "/manage-api/v1/version/should-update";
        string urlWithVersion = $"{url}?version={version}&deviceCode={deviceCode}";

        HttpResponseMessage response = await client.GetAsync(urlWithVersion);

        if (response.IsSuccessStatusCode)
        {
            string shouldUpdate = await response.Content.ReadAsStringAsync();
            return shouldUpdate != "false";
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode}");
            return true;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"Error: {e.Message}");
        return false;
    }
}

static async Task<bool> CheckUpdate(HttpClient client, string url, string currentVersion)
{
    using var response = await client.GetAsync(url);
    
    
    //string zipFilePath = Path.Combine(Path.GetTempPath(), "app.zip");
    //return string.Compare(currentVersion, latestVersion) < 0;
    return true;
}

static async Task<string> DownloadAppAsync(HttpClient client, string baseUrl)
{
    string url = baseUrl + "/manage-api/v1/version/download_latest";
    string zipFilePath = Path.Combine(Path.GetTempPath(), "app.zip");

    using var response = await client.GetAsync(url);
    //check response if successful 
    if (response.IsSuccessStatusCode)
    {
        using var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream);
    }
    else
    {
        return null;
    }
    

    return zipFilePath;
}


static void GrantWriteAccess(string directoryPath, string userOrGroupName)
{
    // Get the DirectoryInfo object for the directory.
    DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);

    // Get the current access control settings for the directory.
    DirectorySecurity directorySecurity = directoryInfo.GetAccessControl();

    // Add the FileSystemAccessRule to grant write access to the specified user or group.
    FileSystemAccessRule fileSystemAccessRule = new FileSystemAccessRule(userOrGroupName, FileSystemRights.Write, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
    directorySecurity.AddAccessRule(fileSystemAccessRule);

    // Apply the new access control settings to the directory.
    directoryInfo.SetAccessControl(directorySecurity);
}

static bool ShouldDownload(string latestVersion, string clickOnceApplicationVersion)
{
    return !string.Equals(latestVersion, clickOnceApplicationVersion);
}

static string GetClickOnceApplicationVersion(string manifestFilePath)
{
    if (!System.IO.File.Exists(manifestFilePath))
    {
        Console.WriteLine($"Manifest file not found: {manifestFilePath}");
        return null;
    }

    XDocument manifest = XDocument.Load(manifestFilePath);
    XNamespace asmv1 = "urn:schemas-microsoft-com:asm.v1";
    XElement assemblyIdentity = manifest.Root.Element(asmv1 + "assemblyIdentity");

    if (assemblyIdentity != null)
    {
        XAttribute versionAttribute = assemblyIdentity.Attribute("version");
        if (versionAttribute != null)
        {
            return versionAttribute.Value;
        }
    }

    return null;
}

//static void DecompressApp(string rarFilePath, string appFolderPath)
//{
//    if (Directory.Exists(appFolderPath))
//    {
//        Directory.Delete(appFolderPath, true);
//    }

//    using (var archive = RarArchive.Open(rarFilePath))
//    {
//        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
//        {
//            string destinationPath = Path.Combine(appFolderPath, entry.Key);
//            string destinationDirectory = Path.GetDirectoryName(destinationPath);

//            if (!Directory.Exists(destinationDirectory))
//            {
//                Directory.CreateDirectory(destinationDirectory);
//                GrantWriteAccess(destinationDirectory, "Users"); // You can replace "Users" with the desired user or group name.
//            }

//            entry.WriteToFile(destinationPath, new ExtractionOptions
//            {
//                ExtractFullPath = true,
//                Overwrite = true
//            });
//        }
//    }
//}

static bool IsValidZipFile(string filePath)
{
    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
    {
        byte[] fileSignature = new byte[4];
        fileStream.Read(fileSignature, 0, 4);
        return fileSignature[0] == 0x50 && fileSignature[1] == 0x4B && fileSignature[2] == 0x03 && fileSignature[3] == 0x04;
    }
}

static void DecompressApp(string zipFilePath, string appFolderPath)
{
    bool isValid = IsValidZipFile(zipFilePath);
    if (Directory.Exists(appFolderPath))
    {
        Directory.Delete(appFolderPath, true);
    }

    // Create the appFolderPath if it doesn't exist
    Directory.CreateDirectory(appFolderPath);

    // Extract the contents of the zip file to the appFolderPath
    ZipFile.ExtractToDirectory(zipFilePath, appFolderPath);
}

//static void RunApplicationManifest(string manifestFilePath)
//{
//    if (!System.IO.File.Exists(manifestFilePath))
//    {
//        Console.WriteLine($"Manifest file not found: {manifestFilePath}");
//        return;
//    }

//    // Here, you can add code to process the application manifest file as needed
//}


//static void RunApplicationManifest(string appFolderPath)
//{
//    string manifestFilePath = FindApplicationFile(appFolderPath);

//    if (manifestFilePath == null)
//    {
//        Console.WriteLine($"Manifest file not found in {appFolderPath}");
//        return;
//    }

//    string dfshimPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dfshim.dll");

//    if (!System.IO.File.Exists(dfshimPath))
//    {
//        Console.WriteLine($"dfshim.dll not found in {Environment.SpecialFolder.System}");
//        return;
//    }

//    ProcessStartInfo startInfo = new ProcessStartInfo("rundll32.exe");
//    startInfo.Arguments = $"\"{dfshimPath}\",ShOpenVerbApplication \"{manifestFilePath}\"";
//    startInfo.WorkingDirectory = appFolderPath;
//    Process.Start(startInfo);
//}

static void RunApplicationManifest(string appFolderPath)
{
    string manifestFilePath = FindApplicationFile(appFolderPath);

    if (manifestFilePath == null)
    {
        Console.WriteLine($"Manifest file not found in {appFolderPath}");
        return;
    }

    try
    {
        string tempBatchFilePath = Path.Combine(Path.GetTempPath(), "launch_clickonce_app.bat");
        System.IO.File.WriteAllText(tempBatchFilePath, $"start \"\" \"{manifestFilePath}\"{Environment.NewLine}");

        ProcessStartInfo startInfo = new ProcessStartInfo(tempBatchFilePath);
        startInfo.WorkingDirectory = appFolderPath;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        Process process = Process.Start(startInfo);
        process.WaitForExit();

        System.IO.File.Delete(tempBatchFilePath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static string FindApplicationFile(string appFolderPath)
{
    if (!Directory.Exists(appFolderPath))
    {
        return null;
    }

    var searchPaths = new List<string>
    {
        appFolderPath,
    };

    foreach (string path in searchPaths)
    {
        var manifestFiles = Directory.GetFiles(path, "*.application");

        if (manifestFiles.Length > 0)
        {
            return manifestFiles[0]; // Return the first manifest file found
        }
    }

    return null;
}

static string GetAppExecutablePathFromManifest(string manifestFilePath)
{
    var manifest = XDocument.Load(manifestFilePath);
    var appElement = manifest.Element("Application");
    var executablePath = appElement?.Element("Executable")?.Value;

    return Path.Combine(Path.GetDirectoryName(manifestFilePath), executablePath);
}

static void OpenApplication(string appExecutablePath)
{
    if (!System.IO.File.Exists(appExecutablePath))
    {
        Console.WriteLine($"Executable file not found: {appExecutablePath}");
        return;
    }

    var processStartInfo = new ProcessStartInfo(appExecutablePath);
    Process.Start(processStartInfo);
}

static void AddToStartup()
{
    string appDirectory = AppContext.BaseDirectory;
    string appName = Path.GetFileNameWithoutExtension(System.Reflection.Assembly.GetExecutingAssembly().Location);
    string appPath = Path.Combine(appDirectory, $"{appName}.exe");

    RegistryKey reg = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
    reg.SetValue(appName, appPath);
}



// Add all the other methods as provided before, but without the "private" access modifier

await Main(args);