using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ReferenceHouseTools
{
    [InitializeOnLoad]
    public static class ReferenceHouseBridge
    {
        static readonly string Folder=Path.GetFullPath("Blender/ReferenceHouse");
        static double nextPoll;
        static ReferenceHouseBridge()
        {
            EditorApplication.update+=Poll;
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder,"Unity_BridgeReady.txt"),DateTime.UtcNow.ToString("O")+"\n"+Application.unityVersion);
        }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            nextPoll=EditorApplication.timeSinceStartup+1;
            string request=Path.Combine(Folder,"Unity_Request.txt");
            if(!File.Exists(request))return;
            string cmd=File.ReadAllText(request).Trim();File.Delete(request);
            if(cmd=="build")ReferenceHouseImporter.Build();
            else if(cmd=="capture")ReferenceHouseImporter.CaptureExisting();
            else if(cmd=="check")ReferenceHouseImporter.CompileCheck();
        }
    }
}
