﻿using System;
using System.IO;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Support.V4.App;
using Android.Support.V7.App;
using Android.Util;
using Android.Widget;
using Org.Opencv.Android;

namespace CustomVision
{

    [Activity(Label = "@string/app_name", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/MyTheme")]
    internal class StartActivity : AppCompatActivity, ILoaderCallbackInterface
    {
        public static int PERMISSION_ALL = 0;
        private Button startButton, startButtonTutorial, startButtonBenchmark;
        private Button StartButton => startButton ?? 
            (startButton = FindViewById<Button>(Resource.Id.start_btn));
        private Button StartButtonTutorial => startButtonTutorial ?? 
            (startButtonTutorial = FindViewById<Button>(Resource.Id.start_btn_tutorial));
        private Button StartButtonBenchmark => startButtonBenchmark ??
            (startButtonBenchmark = FindViewById<Button>(Resource.Id.start_btn_benchmark));

        private static readonly string[] permissions = {
            Manifest.Permission.WriteExternalStorage,
            Manifest.Permission.Camera
        };

        private static readonly string FOLDER_NAME = "/CustomVision";
        private static int IMAGE_FOLDER_COUNT = 1;
        private string sdcardPath = "";

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            if (!OpenCVLoader.InitDebug())
            {
                Log.Debug("Iowa", "Internal OpenCV library not found. Using OpenCV Manager for initialization");
                OpenCVLoader.InitAsync(OpenCVLoader.OpencvVersion300, this, this);
            } else
            {
                Log.Debug("Iowa", "OpenCV library found inside package. Using it!");
                OnManagerConnected(LoaderCallbackInterface.Success);
            }
            ActivityCompat.RequestPermissions(this, permissions, PERMISSION_ALL);
            SetContentView(Resource.Layout.StartScreen);
           
            StartButton.Click += StartButton_Click;
            StartButtonTutorial.Click += StartButtonNoVideo_Click;
            StartButtonBenchmark.Click += StartButtonBenchmark_Click;

            sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path +
                FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
            if(Directory.Exists(sdcardPath))
            {
                while (Directory.Exists(sdcardPath))
                {
                    IMAGE_FOLDER_COUNT += 1;
                    sdcardPath = Android.OS.Environment.ExternalStorageDirectory.Path +
                        FOLDER_NAME + "/" + IMAGE_FOLDER_COUNT;
                }
            }
            if(!Directory.Exists(sdcardPath))
            {
                if(Android.Support.V4.Content.ContextCompat.CheckSelfPermission(this,
                    Manifest.Permission.WriteExternalStorage) == Permission.Granted)
                {
                    Directory.CreateDirectory(sdcardPath);
                }
            }
        }

        private void StartButtonBenchmark_Click(object sender, EventArgs e)
        {
            AssetManager assetManager = Application.Context.Assets;
            string[] assetsList = assetManager.List("");
            int idx = 0;
            MainActivity.sdCardPath = sdcardPath;

            foreach(string fileName in assetsList)
            {
                if(fileName.Contains(".png"))
                {
                    Bitmap test = getBitmapFromAssets(fileName);
                    MainActivity.ImplementImageProcessing(test, idx, false);
                    // save output image
                    MemoryStream byteArrayOutputStream = new MemoryStream();
                    test.Compress(Bitmap.CompressFormat.Png, 100,
                            byteArrayOutputStream);
                    byte[] png = byteArrayOutputStream.ToArray();
                    MainActivity.SaveBitmap(png, idx);
                    test.Dispose(); //release the memory to handle OutOfMemory error
                    idx++;
                }
            }
            System.Environment.Exit(0);
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            Intent m_activity = new Intent(this, typeof(MainActivity));
            m_activity.PutExtra("wait", true);
            m_activity.PutExtra("sdCardPath", sdcardPath);
            StartActivity(m_activity);
        }

        private void StartButtonNoVideo_Click(object sender, EventArgs e)
        {
            Intent m_activity = new Intent(this, typeof(MainActivity));
            m_activity.PutExtra("wait", false);
            m_activity.PutExtra("sdCardPath", sdcardPath);
            StartActivity(m_activity);
        }

        public void OnManagerConnected(int p0)
        {
            switch(p0)
            {
                case LoaderCallbackInterface.Success:
                    Log.Debug("Iowa", "OpenCV loaded successfully");
                    break;
                default:
                    break;
            }
        }

        public void OnPackageInstall(int p0, IInstallCallbackInterface p1)
        {
            throw new NotImplementedException();
        }

        private Bitmap getBitmapFromAssets(string fileName)
        {
            AssetManager assetManager = Application.Context.Assets;
            Stream istr = assetManager.Open(fileName);
            Bitmap bitmap = BitmapFactory.DecodeStream(istr);
            istr.Close();
            return bitmap;
        }
    }
}