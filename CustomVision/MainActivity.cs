using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Support.V7.App;
using Android.Views;
using Android.Util;
using System.IO;
using Android.Support.V4.App;
using Android;
using Android.Hardware.Camera2;
using Android.Graphics;
using System.Collections.Generic;
using Android.Media;
using System.Threading;

namespace CustomVision
{
    internal class StateCallback : CameraDevice.StateCallback
    {

        public StateCallback()
        {
        }

        public override void OnOpened(CameraDevice camera)
        {
            MainActivity.cameraDevice = camera;
            if(MainActivity.cameraDevice != null)
            {
                MainActivity.CreatePreviewSession();
            }
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            MainActivity.cameraDevice = null;
        }

        public override void OnError(CameraDevice camera, Android.Hardware.Camera2.CameraError error)
        {
            camera.Close();
            MainActivity.cameraDevice = null;
        }
    }

    internal class CameraCaptureStateCallback : CameraCaptureSession.StateCallback
    {
        private CaptureRequest captureRequest;

        public override void OnConfigured(CameraCaptureSession session)
        {
            if (MainActivity.cameraDevice == null)
            {
                return;
            }

            try
            {
                captureRequest = MainActivity.captureRequestBuilder.Build();
                MainActivity.cameraCaptureSession = session;
                MainActivity.cameraCaptureSession.SetRepeatingRequest(captureRequest,
                        null, MainActivity.backgroundHandler);
                Interlocked.Exchange(ref MainActivity.canProcessImage, 1);
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }

        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            session.Close();
        }
    }

    [Activity(Label = "@string/app_name", MainLauncher = false, Icon = "@mipmap/icon", Theme = "@style/MyTheme", ScreenOrientation = ScreenOrientation.Portrait)]
    public class MainActivity : AppCompatActivity, TextureView.ISurfaceTextureListener
    {
        private CameraDevice.StateCallback stateCallback;
        public static CameraCaptureSession.StateCallback cameraCaptureStateCallback;
        private CameraManager cameraManager;
        private int cameraFacing;
        public static TextureView textureView;
        private static readonly string FOLDER_NAME = "/CustomVision";
        private static int PREFIX = 1;
        private static int IMAGE_FOLDER_COUNT = 1;
        private static readonly ImageClassifier imageClassifier = new ImageClassifier();
        public static int CAMERA_REQUEST = 0;
        public static Size previewSize;
        public static ImageReader imageReader;
        internal static CameraDevice cameraDevice;
        private HandlerThread backgroundThread;
        public static Handler backgroundHandler;
        public static CaptureRequest.Builder captureRequestBuilder;
        internal static CameraCaptureSession cameraCaptureSession;
        public string CameraId { get; private set; }
        private int DSI_height;
        private int DSI_width;
        public static int canProcessImage = 0;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            stateCallback = new StateCallback();
            cameraCaptureStateCallback = new CameraCaptureStateCallback();
            ActivityCompat.RequestPermissions(this, new string[] { Manifest.Permission.Camera }, CAMERA_REQUEST);
            cameraManager = (CameraManager)GetSystemService(CameraService);
            cameraFacing = (int)LensFacing.Back;
            textureView = new TextureView(this);
            SetContentView(textureView);
            Window.SetFlags(WindowManagerFlags.KeepScreenOn, WindowManagerFlags.KeepScreenOn);
            string sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;

            if (Directory.Exists(sdcardPath))
            {
                while (Directory.Exists(sdcardPath))
                {
                    IMAGE_FOLDER_COUNT += 1;
                    sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path + FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
                }
            }
            if (!Directory.Exists(sdcardPath))
            {
                Directory.CreateDirectory(sdcardPath);
            }

        }

        protected override void OnResume()
        {
            base.OnResume();
            OpenBackgroundThread();
            if(textureView.IsAvailable)
            {
                SetUpCamera();
                OpenCamera();
            } else
            {
                textureView.SurfaceTextureListener = this;
            }
        }

        protected override void OnStop()
        {
            base.OnStop();
            CloseCamera();
            CloseBackgroundThread();
        }

        private void CloseCamera()
        {
            if (cameraCaptureSession != null)
            {
                cameraCaptureSession.Close();
                cameraCaptureSession = null;
            }

            if (cameraDevice != null)
            {
                cameraDevice.Close();
                cameraDevice = null;
            }
        }

        public static void CreatePreviewSession()
        {
            try
            {
                SurfaceTexture surfaceTexture = textureView.SurfaceTexture;
                surfaceTexture.SetDefaultBufferSize(previewSize.Width, previewSize.Height);
                Surface previewSurface = new Surface(surfaceTexture);
                captureRequestBuilder = cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
                captureRequestBuilder.AddTarget(previewSurface);
                captureRequestBuilder.AddTarget(imageReader.Surface);
                cameraDevice.CreateCaptureSession(new List<Surface>() { previewSurface, imageReader.Surface }, 
                    cameraCaptureStateCallback, backgroundHandler);
            } catch (CameraAccessException e) {
                e.PrintStackTrace();
            }
        }

        private void OpenBackgroundThread()
        {
            backgroundThread = new HandlerThread("camera_background_thread");
            backgroundThread.Start();
            backgroundHandler = new Handler(backgroundThread.Looper);
        }

        private void CloseBackgroundThread()
        {
            if (backgroundHandler != null)
            {
                backgroundThread.QuitSafely();
                backgroundThread = null;
                backgroundHandler = null;
            }
        }

        private void SetAspectRatioTextureView(int ResolutionWidth, int ResolutionHeight)
        {
            if (ResolutionWidth > ResolutionHeight)
            {
                int newWidth = DSI_width;
                int newHeight = ((DSI_width * ResolutionWidth) / ResolutionHeight);
                UpdateTextureViewSize(newWidth, newHeight);

            }
            else
            {
                int newWidth = DSI_width;
                int newHeight = DSI_width * ResolutionHeight / ResolutionWidth;
                UpdateTextureViewSize(newWidth, newHeight);
            }
        }

        public static void UpdateTextureViewSize(int viewWidth, int viewHeight)
        {
            textureView.LayoutParameters.Width = viewWidth;
            textureView.LayoutParameters.Height= viewHeight;
            textureView.RequestLayout();
        }

        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int w, int h)
        {
            SetUpCamera();
            OpenCamera();
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
        {
            return false;
        }

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
        {
            // camera takes care of this
        }

        public void OnSurfaceTextureUpdated(SurfaceTexture surface)
        {
        }

        public static async void RecognizeImage(Bitmap rgbBitmap, byte[] data)
        {
            string result = await Task.Run(() => imageClassifier.RecognizeImage(rgbBitmap));
            SaveBitmap(result, data);
        }

        private static void SaveBitmap(string label, byte[] data) {
            DateTime currentDate = DateTime.Now;
            long ts = currentDate.Ticks;
            SaveLog(label, currentDate, PREFIX);
            string sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path+FOLDER_NAME+"/"+IMAGE_FOLDER_COUNT;
            string fileName = PREFIX + ".  " + currentDate.TimeOfDay + "_" + label + ".png";
            string FilePath = System.IO.Path.Combine(sdcardPath, fileName);

            if (!File.Exists(FilePath))
            {
                File.WriteAllBytes(FilePath, data);
            }

            PREFIX = PREFIX + 1;
        }

        private static void SaveLog(string label, DateTime currentTime, int prefix)
        {
            string msg = prefix + ".  " + currentTime.TimeOfDay + "_" + label;
            string sdCardPath = Android.OS.Environment.ExternalStorageDirectory.Path+FOLDER_NAME+"/"+IMAGE_FOLDER_COUNT;
            string filePath = System.IO.Path.Combine(sdCardPath, "log.txt");
            if (!File.Exists(filePath))
            {
                using (StreamWriter write = new StreamWriter(filePath, true))
                {
                    write.Write(msg+"\n");
                }

            } else
            {
                using (StreamWriter write = new StreamWriter(filePath, true))
                {
                    write.Write(msg + "\n");
                }
            }
            
        }

        private void SetUpCamera()
        {
            try {
                foreach (string cameraId in cameraManager.GetCameraIdList()) {
                    CameraCharacteristics cameraCharacteristics =
                            cameraManager.GetCameraCharacteristics(cameraId);
                    if ((int)cameraCharacteristics.Get(CameraCharacteristics.LensFacing) == cameraFacing) {
                        Android.Hardware.Camera2.Params.StreamConfigurationMap streamConfigurationMap =
                            (Android.Hardware.Camera2.Params.StreamConfigurationMap)cameraCharacteristics.Get(
                                CameraCharacteristics.ScalerStreamConfigurationMap);
                        DisplayMetrics displayMetrics = Resources.DisplayMetrics;
                        DSI_height = displayMetrics.HeightPixels;
                        DSI_width = displayMetrics.WidthPixels;
                        previewSize = streamConfigurationMap.GetOutputSizes((int)ImageFormatType.Jpeg)[0];
                        SetAspectRatioTextureView(previewSize.Width, previewSize.Height);
                        imageReader = ImageReader.NewInstance(previewSize.Width, previewSize.Height, ImageFormatType.Yuv420888, 1);
                        imageReader.SetOnImageAvailableListener(new ImageAvailableListener(), backgroundHandler);
                        CameraId = cameraId;
                    }
                } 
            } catch (CameraAccessException e) {
                e.PrintStackTrace();
            }
        }

        private void OpenCamera()
        {
            try
            {
                if (Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera)
                        == Permission.Granted)
                {
                    cameraManager.OpenCamera(CameraId, stateCallback, backgroundHandler);
                }
            }
            catch (CameraAccessException e)
            {
                e.PrintStackTrace();
            }
        }
    }

    internal class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader reader)
        {
            if (1 == Interlocked.CompareExchange(ref MainActivity.canProcessImage, 0, 1))
            {
                Image image = reader.AcquireNextImage();
                // Process the image
                if (image == null)
                {
                    return;
                }

                int width = MainActivity.textureView.Width;
                int height = MainActivity.textureView.Height;
                Bitmap bitmap = MainActivity.textureView.GetBitmap(width, height);
                image.Close();
                MemoryStream byteArrayOutputStream = new MemoryStream();
                Bitmap cropped = Bitmap.CreateBitmap(bitmap, 0, 0, width, height);
                cropped.Compress(Bitmap.CompressFormat.Png, 100, byteArrayOutputStream);
                byte[] png = byteArrayOutputStream.ToArray();
                MainActivity.RecognizeImage(bitmap, png);
                Interlocked.Exchange(ref MainActivity.canProcessImage, 1);
            }
        }
    }
}


