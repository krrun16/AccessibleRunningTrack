using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Android.App;
using Android.Graphics;
using Org.Tensorflow.Contrib.Android;

namespace CustomVision
{
    public class ImageClassifier
    {
        private readonly List<string> labels;
        private readonly TensorFlowInferenceInterface inferenceInterface;
        private static readonly int InputSize = 227;
        private static readonly string InputName = "Placeholder";
        private static readonly string OutputName = "loss";

        public ImageClassifier()
        {
            Android.Content.Res.AssetManager assets = Application.Context.Assets;
			inferenceInterface = new TensorFlowInferenceInterface(assets, "model.pb");

            using (StreamReader sr = new StreamReader(assets.Open("labels.txt")))
            {
                string content = sr.ReadToEnd();
                labels = content.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }
        }

        public string RecognizeImage(Bitmap bitmap, int prefix)
        {
            string[] outputNames = new[] { OutputName };
            float[] floatValues = GetBitmapPixels(bitmap);
            float[] outputs = new float[labels.Count];

            inferenceInterface.Feed(InputName, floatValues, 1, InputSize, InputSize, 3);
            inferenceInterface.Run(outputNames);
            inferenceInterface.Fetch(OutputName, outputs);

            List<Tuple<float, string>> results = new List<Tuple<float, string>>();
            for (int i = 0; i < outputs.Length; ++i)
            {
                results.Add(Tuple.Create(outputs[i], labels[i]));
            }
            IOrderedEnumerable<Tuple<float, string>> orderedResults = 
                results.OrderByDescending(t => t.Item1);
            string orderedResultsMsg = "";
            for (int i = 0; i < orderedResults.Count(); i++)
            {
                orderedResultsMsg += orderedResults.ElementAt(i).Item2 + ": " 
                    + orderedResults.ElementAt(i).Item1 + "; ";
            }
            MainActivity.SaveLog(orderedResultsMsg, DateTime.Now, prefix);
            if (orderedResults.First().Item1 > .8)
            {
                return orderedResults.First().Item2;
            } else
            {
                return "unknown";
            }
            
        }

        private static float[] GetBitmapPixels(Bitmap bitmap)
        {
            float[] floatValues = new float[InputSize * InputSize * 3];

            using (Bitmap scaledBitmap = Bitmap.CreateScaledBitmap(bitmap, InputSize, InputSize, false))
            {
                using (Bitmap resizedBitmap = scaledBitmap.Copy(Bitmap.Config.Argb8888, false))
                {
                    int[] intValues = new int[InputSize * InputSize];
                    resizedBitmap.GetPixels(intValues, 0, resizedBitmap.Width, 0, 0, resizedBitmap.Width, resizedBitmap.Height);

                    for (int i = 0; i < intValues.Length; ++i)
                    {
                        int val = intValues[i];

                        floatValues[(i * 3) + 0] = (val & 0xFF) - 104;
                        floatValues[(i * 3) + 1] = ((val >> 8) & 0xFF) - 117;
                        floatValues[(i * 3) + 2] = ((val >> 16) & 0xFF) - 123;
                    }

                    resizedBitmap.Recycle();
                }

                scaledBitmap.Recycle();
            }

            return floatValues;
        }
    }
}
