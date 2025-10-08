using UnityEngine;
using System.IO;

public class SceneCapturer : MonoBehaviour
{
    [Header("Capture Settings")]
    public Camera captureCamera; // 캡처에 사용할 카메라 (자기 자신을 연결)
    public int imageWidth = 1200;
    public int imageHeight = 1024;
    public string folderName = "SceneCaptures";
    void Awake()
    {
        // 만약 카메라가 할당되지 않았다면, 자기 자신에게 붙어있는 카메라를 사용
        if (captureCamera == null)
        {
            captureCamera = GetComponent<Camera>();
        }
        // 평소에는 이 카메라를 꺼둡니다.
        captureCamera.enabled = false;
    }

    /// <summary>
    /// 외부(컨트롤러)에서 이 함수를 호출하여 스크린샷을 찍고 저장합니다.
    /// </summary>
    /// <param name="fileName">저장할 파일 이름 (확장자 제외)</param>
    public void CaptureScene(string fileName)
    {
        // 캡처를 위한 임시 렌더 텍스처 생성
        RenderTexture renderTexture = new RenderTexture(imageWidth, imageHeight, 24)
        {
            name = "RT_" + gameObject.name
        };
        captureCamera.targetTexture = renderTexture;

        // 텍스처를 담을 2D 텍스처 생성
        Texture2D screenshot = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);

        // 카메라를 수동으로 렌더링
        captureCamera.Render();

        // 현재 활성화된 렌더 텍스처를 읽어옴
        RenderTexture.active = renderTexture;
        screenshot.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
        screenshot.Apply();

        // 사용이 끝난 텍스처들을 정리
        captureCamera.targetTexture = null;
        RenderTexture.active = null;
        Destroy(renderTexture);

        // 파일로 변환 및 저장
        byte[] bytes = screenshot.EncodeToJPG();
        string folderPath = Path.Combine(Application.dataPath, "..", folderName);
        Directory.CreateDirectory(folderPath); // 폴더가 없으면 생성

        string fullPath = Path.Combine(folderPath, fileName + ".jpg");
        File.WriteAllBytes(fullPath, bytes);
        Debug.Log($"Scene captured and saved to {fullPath}");
    }
}
