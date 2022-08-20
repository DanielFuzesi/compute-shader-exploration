using UnityEngine;

// Class to convert a Texture2D to a RenderTexture
public static class TextureToRenderTexture
{
    // Method to convert Texture2D to RenderTexture format
    public static void ConvertTexture2dToRenderTexture(in Texture2D inputTex, out RenderTexture rendTex, int res) {
        rendTex = new RenderTexture(res, res, 0);
        rendTex.enableRandomWrite = true;
        RenderTexture.active = rendTex;

        Graphics.Blit(inputTex, rendTex);
    }

}
