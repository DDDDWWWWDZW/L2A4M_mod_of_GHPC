using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leo2A4M
{
    public class MatInfo
    {
        public string Name;
        public string Albedo;
        public string Normal;
        public string Spec;
        public string Ao;
        public string Alpha;
        public bool Transparent;
    }

    public class ObjPart
    {
        public string Name;
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly Dictionary<string, List<int>> Tris = new Dictionary<string, List<int>>();
    }

    public static class ObjLoader
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static Dictionary<string, MatInfo> LoadMatInfo(string tsvPath)
        {
            var result = new Dictionary<string, MatInfo>();
            if (!File.Exists(tsvPath))
                return result;

            string[] lines = File.ReadAllLines(tsvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] c = line.Split('\t');
                if (c.Length < 7)
                    continue;

                result[c[0]] = new MatInfo
                {
                    Name = c[0],
                    Albedo = c[1],
                    Normal = c[2],
                    Spec = c[3],
                    Ao = c[4],
                    Alpha = c[5],
                    Transparent = c[6] == "1",
                };
            }
            return result;
        }

        /// <summary>parts.tsv maps every OBJ object to the rig anchor it rides on
        /// (Hull / Turret / Gun).  Keeping the pieces separate is what allows per-piece
        /// armour values and AAR/damage visuals later.</summary>
        public static Dictionary<string, string> LoadParts(string tsvPath)
        {
            var result = new Dictionary<string, string>();
            if (!File.Exists(tsvPath))
                return result;

            string[] lines = File.ReadAllLines(tsvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string[] c = line.Split('\t');
                if (c.Length < 2)
                    continue;
                result[c[0]] = c[1];
            }
            return result;
        }

        private static int Index(string token, int count)
        {
            if (string.IsNullOrEmpty(token))
                return -1;
            token = token.Trim();
            if (token.Length == 0)
                return -1;
            int i;
            if (!int.TryParse(token, System.Globalization.NumberStyles.Integer, Inv, out i))
            {
                MelonLoader.MelonLogger.Warning("[Leo2A4M] bad index token: '" + token + "'");
                return -1;
            }
            return i > 0 ? i - 1 : count + i;
        }

        private static Vector3 V3(string[] parts, int offset)
        {
            return new Vector3(
                float.Parse(parts[offset].Trim(), Inv),
                float.Parse(parts[offset + 1].Trim(), Inv),
                float.Parse(parts[offset + 2].Trim(), Inv));
        }

        private static Vector2 V2(string[] parts, int offset)
        {
            return new Vector2(
                float.Parse(parts[offset].Trim(), Inv),
                float.Parse(parts[offset + 1].Trim(), Inv));
        }

        public static List<ObjPart> Parse(string objPath)
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var parts = new List<ObjPart>();
            ObjPart cur = null;
            string mat = "default";

            using (var sr = new StreamReader(objPath))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length < 2)
                        continue;

                    if (line[0] == 'v' && line[1] == ' ')
                    {
                        positions.Add(V3(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries), 1));
                    }
                    else if (line[0] == 'v' && line[1] == 't')
                    {
                        uvs.Add(V2(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries), 1));
                    }
                    else if (line[0] == 'v' && line[1] == 'n')
                    {
                        normals.Add(V3(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries), 1));
                    }
                    else if (line[0] == 'o' && line[1] == ' ')
                    {
                        cur = new ObjPart { Name = line.Substring(2).Trim() };
                        parts.Add(cur);
                    }
                    else if (line.StartsWith("usemtl ", StringComparison.Ordinal))
                    {
                        mat = line.Substring(7).Trim();
                    }
                    else if (line[0] == 'f' && line[1] == ' ')
                    {
                        if (cur == null)
                        {
                            cur = new ObjPart { Name = "default" };
                            parts.Add(cur);
                        }

                        string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int k = 3; k < t.Length; k++)
                            AddTriangle(cur, mat, t[1], t[k - 1], t[k], positions, uvs, normals);
                    }
                }
            }

            // Drop empty groups.
            parts.RemoveAll(p => p.Vertices.Count == 0 || p.Tris.Count == 0);
            return parts;
        }

        private static void AddTriangle(
            ObjPart part, string mat, string a, string b, string c,
            List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals)
        {
            string[] ca = a.Split('/');
            string[] cb = b.Split('/');
            string[] cc = c.Split('/');

            int pa = Index(ca[0], positions.Count);
            int pb = Index(cb[0], positions.Count);
            int pc = Index(cc[0], positions.Count);
            if (pa < 0 || pb < 0 || pc < 0)
                return;

            int ta = ca.Length > 1 ? Index(ca[1], uvs.Count) : -1;
            int tb = cb.Length > 1 ? Index(cb[1], uvs.Count) : -1;
            int tc = cc.Length > 1 ? Index(cc[1], uvs.Count) : -1;

            int na = ca.Length > 2 ? Index(ca[2], normals.Count) : -1;
            int nb = cb.Length > 2 ? Index(cb[2], normals.Count) : -1;
            int nc = cc.Length > 2 ? Index(cc[2], normals.Count) : -1;

            Vector3 p0 = positions[pa];
            Vector3 p1 = positions[pb];
            Vector3 p2 = positions[pc];
            Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0).normalized;

            int i0 = AddCorner(part, p0, ta >= 0 ? uvs[ta] : Vector2.zero, na >= 0 ? normals[na] : faceNormal, mat);
            int i1 = AddCorner(part, p1, tb >= 0 ? uvs[tb] : Vector2.zero, nb >= 0 ? normals[nb] : faceNormal, mat);
            int i2 = AddCorner(part, p2, tc >= 0 ? uvs[tc] : Vector2.zero, nc >= 0 ? normals[nc] : faceNormal, mat);

            List<int> list;
            if (!part.Tris.TryGetValue(mat, out list))
            {
                list = new List<int>();
                part.Tris[mat] = list;
            }
            list.Add(i0);
            list.Add(i1);
            list.Add(i2);
        }

        private static int AddCorner(ObjPart part, Vector3 p, Vector2 uv, Vector3 n, string mat)
        {
            part.Vertices.Add(p);
            part.Uvs.Add(uv);
            part.Normals.Add(n);
            return part.Vertices.Count - 1;
        }

        private static readonly Dictionary<string, Texture2D> TextureCache =
            new Dictionary<string, Texture2D>();

        private static Texture2D LoadTexture(string path, bool linear)
        {
            if (!File.Exists(path))
                return null;

            string key = path + "|" + linear;
            Texture2D cached;
            if (TextureCache.TryGetValue(key, out cached))
                return cached;

            string paint = Leo2A4MMod.PaintScheme != null ? (Leo2A4MMod.PaintScheme.Value ?? "Olive").Trim().ToLowerInvariant() : "olive";
            bool tri = paint == "tricolor" || paint == "heavycavalry" || paint == "heavy cavalry";
            if (tri)
            {
                string mapped = path.Replace("_c_us_camo_modern_green", "_c_au_camo_tricolor")
                    .Replace("_c_ger_camo_inside_elfebein", "_c_au_camo_tricolor");
                if (File.Exists(mapped))
                    path = mapped;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
                return null;

            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;

            string lowerPath = path.ToLowerInvariant();
            bool detailTexture = lowerPath.Contains("glass") || lowerPath.Contains("headlight") ||
                lowerPath.Contains("chain") || lowerPath.Contains("net") || lowerPath.Contains("track");
            if (!linear && !detailTexture)
                ApplyTextureLook(tex);

            tex.mipMapBias = Leo2A4MMod.TextureMipBias != null ? Leo2A4MMod.TextureMipBias : 0f;
            tex.Apply(true, false);

            TextureCache[key] = tex;
            return tex;
        }

        private static void ApplyTextureLook(Texture2D tex)
        {
            float saturation = Leo2A4MMod.TextureSaturation != null ? Leo2A4MMod.TextureSaturation : 1f;
            float brightness = Leo2A4MMod.TextureBrightness != null ? Leo2A4MMod.TextureBrightness : 1f;
            float contrast = Leo2A4MMod.TextureContrast != null ? Leo2A4MMod.TextureContrast : 1f;
            float clean = Leo2A4MMod.TextureClean != null ? Mathf.Clamp01(Leo2A4MMod.TextureClean) : 0f;
            float green = Leo2A4MMod.TextureGreenStrength != null ? Mathf.Clamp01(Leo2A4MMod.TextureGreenStrength) : 0f;
            string scheme = Leo2A4MMod.PaintScheme != null ? (Leo2A4MMod.PaintScheme.Value ?? "Olive").Trim().ToLowerInvariant() : "olive";
            bool tricolor = scheme == "tricolor" || scheme == "heavycavalry" || scheme == "heavy cavalry";
            bool sand = scheme == "sand" || scheme == "desert";
            if (tricolor)
            {
                clean = Mathf.Clamp01(clean * 0.55f);
                contrast = Mathf.Min(contrast, 0.90f);
                brightness *= 1.13f;
                saturation = Mathf.Max(saturation, 1.05f);
            }
            if (sand)
            {
                clean = Mathf.Clamp01(clean * 0.25f);
                contrast = Mathf.Max(contrast, 0.98f);
            }

            if (Mathf.Approximately(saturation, 1f) && Mathf.Approximately(brightness, 1f) &&
                Mathf.Approximately(contrast, 1f) && clean <= 0.001f && green <= 0.001f && !sand && !tricolor)
                return;

            Color32[] pixels = tex.GetPixels32();
            Color32[] blurred = clean > 0.01f ? BoxBlur(pixels, tex.width, tex.height) : null;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 c = pixels[i];
                float r = c.r / 255f;
                float g = c.g / 255f;
                float b = c.b / 255f;

                if (blurred != null)
                {
                    r = Mathf.Lerp(r, blurred[i].r / 255f, clean);
                    g = Mathf.Lerp(g, blurred[i].g / 255f, clean);
                    b = Mathf.Lerp(b, blurred[i].b / 255f, clean);
                }

                r = (r - 0.5f) * contrast + 0.5f;
                g = (g - 0.5f) * contrast + 0.5f;
                b = (b - 0.5f) * contrast + 0.5f;

                float exposure = sand ? brightness * 1.03f : brightness;
                r = Mathf.Clamp01(r * exposure);
                g = Mathf.Clamp01(g * exposure);
                b = Mathf.Clamp01(b * exposure);

                if (tricolor)
                {
                    r = Mathf.Pow(Mathf.Clamp01(r), 0.90f);
                    g = Mathf.Pow(Mathf.Clamp01(g), 0.90f);
                    b = Mathf.Pow(Mathf.Clamp01(b), 0.90f);
                }

                float lum = 0.299f * r + 0.587f * g + 0.114f * b;
                r = Mathf.Clamp01(lum + (r - lum) * saturation);
                g = Mathf.Clamp01(lum + (g - lum) * saturation);
                b = Mathf.Clamp01(lum + (b - lum) * saturation);

                if (sand)
                {
                    float l = Mathf.Clamp01(lum);
                    float darkR = 0.46f, darkG = 0.41f, darkB = 0.31f;
                    float lightR = 0.92f, lightG = 0.82f, lightB = 0.63f;
                    float tr = Mathf.Lerp(darkR, lightR, l);
                    float tg = Mathf.Lerp(darkG, lightG, l);
                    float tb = Mathf.Lerp(darkB, lightB, l);
                    r = tr;
                    g = tg;
                    b = tb;
                }
                else if (green > 0.001f)
                {
                    float shade = 0.55f + 0.90f * lum;
                    float tr = 0.16f * shade;
                    float tg = 0.20f * shade;
                    float tb = 0.09f * shade;
                    r = Mathf.Lerp(r, tr, green);
                    g = Mathf.Lerp(g, tg, green);
                    b = Mathf.Lerp(b, tb, green);
                }

                pixels[i] = new Color32((byte)(r * 255f), (byte)(g * 255f), (byte)(b * 255f), c.a);
            }

            tex.SetPixels32(pixels);
        }

        private static Color32[] BoxBlur(Color32[] src, int width, int height)
        {
            var dst = new Color32[src.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int rs = 0, gs = 0, bs = 0, asum = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = (y + dy + height) % height;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = (x + dx + width) % width;
                            Color32 c = src[ny * width + nx];
                            rs += c.r;
                            gs += c.g;
                            bs += c.b;
                            asum += c.a;
                            n++;
                        }
                    }
                    dst[y * width + x] = new Color32((byte)(rs / n), (byte)(gs / n), (byte)(bs / n), (byte)(asum / n));
                }
            }
            return dst;
        }

        /// <summary>Copy the mask's luminance into the albedo's alpha channel.</summary>
        private static Texture2D CombineAlpha(Texture2D albedo, Texture2D mask)
        {
            int w = albedo.width;
            int h = albedo.height;
            int mw = mask.width;
            int mh = mask.height;
            Color32[] src;
            Color32[] msk;
            try
            {
                src = albedo.GetPixels32();
                msk = mask.GetPixels32();
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[Leo2A4M] alpha combine failed: " + e.Message);
                return albedo;
            }

            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
            {
                int my = Mathf.Clamp((int)((float)y * mh / h), 0, mh - 1);
                for (int x = 0; x < w; x++)
                {
                    int mx = Mathf.Clamp((int)((float)x * mw / w), 0, mw - 1);
                    Color32 c = src[y * w + x];
                    c.a = msk[my * mw + mx].r;
                    dst[y * w + x] = c;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
            tex.name = albedo.name + "_masked";
            tex.SetPixels32(dst);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// Build materials on GHPC's own vehicle shader so the swap shades exactly like a
        /// native tank (albedo / normal / smoothness / occlusion slots).
        /// </summary>
        private static Dictionary<string, Material> BuildVehicleMaterials(
            Dictionary<string, MatInfo> infos, string assetRoot, Shader vehicle)
        {
            var result = new Dictionary<string, Material>();
            Shader std = Shader.Find("Standard");
            int ok = 0;

            foreach (var info in infos.Values)
            {
                bool transparent = info.Transparent;
                var mat = new Material(transparent && std != null ? std : vehicle);
                mat.name = info.Name;

                Texture2D albedo = Tex(assetRoot, info.Albedo, false);
                Texture2D normal = Tex(assetRoot, info.Normal, true);
                Texture2D spec = Tex(assetRoot, info.Spec, true);
                Texture2D ao = Tex(assetRoot, info.Ao, true);
                Texture2D alphaMask = null;
                string albLower = info.Albedo != null ? info.Albedo.ToLowerInvariant() : "";
                bool isGlass = albLower.Contains("glass");
                if (!string.IsNullOrEmpty(info.Alpha) && !isGlass)
                {
                    alphaMask = Tex(assetRoot, info.Alpha, true);
                    if (alphaMask != null && albedo != null)
                        albedo = CombineAlpha(albedo, alphaMask);
                }

                if (!transparent)
                {
                    if (albedo != null)
                        mat.SetTexture("_Albedo", albedo);
                    if (normal != null)
                    {
                        mat.SetTexture("_Normal", normal);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    if (spec != null)
                    {
                        mat.SetTexture("_Smoothness", spec);
                        mat.EnableKeyword("_METALLICGLOSSMAP");
                    }
                    if (ao != null)
                        mat.SetTexture("_Occlusion", ao);
                    if (alphaMask != null)
                    {
                        mat.EnableKeyword("_ALPHATEST_ON");
                        if (mat.HasProperty("_Cutoff"))
                            mat.SetFloat("_Cutoff", 0.5f);
                    }
                    mat.renderQueue = alphaMask != null ? 2450 : 2000;
                }
                else
                {
                    if (albedo != null)
                        mat.SetTexture("_MainTex", albedo);
                    if (normal != null)
                    {
                        mat.SetTexture("_BumpMap", normal);
                        mat.EnableKeyword("_NORMALMAP");
                    }
                    if (alphaMask != null && !isGlass)
                    {
                        mat.SetFloat("_Mode", 1f);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                        mat.SetInt("_ZWrite", 1);
                        mat.EnableKeyword("_ALPHATEST_ON");
                        if (mat.HasProperty("_Cutoff"))
                            mat.SetFloat("_Cutoff", 0.5f);
                        mat.renderQueue = 2450;
                    }
                    else
                    {
                        mat.SetFloat("_Mode", 2f);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.renderQueue = 3000;
                        if (isGlass && mat.HasProperty("_Color"))
                            mat.SetColor("_Color", new Color(0.04f, 0.06f, 0.08f, 0.55f));
                    }
                }
                result[info.Name] = mat;
                ok++;
            }
            MelonLoader.MelonLogger.Msg("[Leo2A4M] vehicle-shader materials=" + ok);
            return result;
        }

        private static Texture2D Tex(string root, string rel, bool linear)
        {
            if (string.IsNullOrEmpty(rel))
                return null;
            return LoadTexture(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)), linear);
        }

        public static Dictionary<string, Material> BuildMaterials(Dictionary<string, MatInfo> infos, string assetRoot)        {
            var result = new Dictionary<string, Material>();
            Shader vehicle = Shader.Find("GHPC/VehicleShader");
            if (vehicle != null && Environment.GetEnvironmentVariable("LEO2A4M_STANDARDMAT") != "1")
                return BuildVehicleMaterials(infos, assetRoot, vehicle);

            Shader flir = Shader.Find("Standard (FLIR)");
            Shader std = Shader.Find("Standard");

            foreach (var info in infos.Values)
            {
                bool transparent = info.Transparent && std != null;
                bool useFlir = Environment.GetEnvironmentVariable("LEO2A4M_STD") != "1";
                Shader shader = transparent || !useFlir ? std : (flir != null ? flir : std);
                if (shader == null)
                {
                    MelonLoader.MelonLogger.Error("[Leo2A4M] no usable shader");
                    continue;
                }

                var mat = new Material(shader);
                mat.name = info.Name;
                string albLower = info.Albedo != null ? info.Albedo.ToLowerInvariant() : "";
                bool isHeadlight = albLower.Contains("headlight");
                bool isGlass = albLower.Contains("glass");
                bool isGrille = info.Name.StartsWith("284dafbb", StringComparison.OrdinalIgnoreCase);

                Texture2D albedo = LoadTexture(Path.Combine(assetRoot, info.Albedo.Replace('/', Path.DirectorySeparatorChar)), false);
                Texture2D normal = LoadTexture(Path.Combine(assetRoot, info.Normal.Replace('/', Path.DirectorySeparatorChar)), true);

                // WT keeps the opacity mask in a separate _a texture. Fold it into the
                // albedo's alpha channel so grilles/nets cut out instead of turning into
                // solid dark sheets.
                Texture2D alphaMap = null;
                if (!string.IsNullOrEmpty(info.Alpha) && !isGlass)
                {
                    alphaMap = LoadTexture(Path.Combine(assetRoot, info.Alpha.Replace('/', Path.DirectorySeparatorChar)), true);
                    if (alphaMap != null && albedo != null)
                        albedo = CombineAlpha(albedo, alphaMap);
                }

                if (albedo != null)
                {
                    mat.mainTexture = albedo;
                    if (mat.HasProperty("_MainTex"))
                        mat.SetTexture("_MainTex", albedo);
                }

                if (normal != null && mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                    if (mat.HasProperty("_BumpScale"))
                        mat.SetFloat("_BumpScale", 1f);
                }

                if (mat.HasProperty("_Cull"))
                    mat.SetFloat("_Cull", 0f);
                if (mat.HasProperty("_Metallic"))
                    mat.SetFloat("_Metallic", 0f);
                if (mat.HasProperty("_Glossiness"))
                    mat.SetFloat("_Glossiness", 0.30f);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", Color.white);

                if (transparent)
                {
                    if (alphaMap != null)
                    {
                        // masked netting / grille: alpha cutout, still depth-writing
                        mat.SetFloat("_Mode", 1f);
                        mat.SetInt("_SrcBlend", (int)BlendMode.One);
                        mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                        mat.SetInt("_ZWrite", 1);
                        mat.EnableKeyword("_ALPHATEST_ON");
                        mat.DisableKeyword("_ALPHABLEND_ON");
                        mat.SetFloat("_Cutoff", 0.5f);
                        mat.SetOverrideTag("RenderType", "TransparentCutout");
                        mat.renderQueue = 2450;
                    }
                    else
                    {
                        mat.SetFloat("_Mode", 2f);
                        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.DisableKeyword("_ALPHATEST_ON");
                        mat.EnableKeyword("_ALPHABLEND_ON");
                        mat.renderQueue = 3000;
                    }
                }

                if (isGlass && !isHeadlight && mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", new Color(0.04f, 0.06f, 0.08f, 0.55f));
                    if (mat.HasProperty("_Glossiness"))
                        mat.SetFloat("_Glossiness", 0.85f);
                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", 0.1f);
                }

                if (isGrille)
                {
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", new Color(0.035f, 0.040f, 0.035f, 1f));
                    if (mat.HasProperty("_Glossiness"))
                        mat.SetFloat("_Glossiness", 0.25f);
                    if (mat.HasProperty("_Metallic"))
                        mat.SetFloat("_Metallic", 0.15f);
                    if (mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                }

                if (isHeadlight)
                {
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", new Color(0.015f, 0.015f, 0.015f, 1f));
                    if (mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                    if (mat.HasProperty("_Glossiness"))
                        mat.SetFloat("_Glossiness", 0.2f);
                    mat.SetFloat("_Mode", 0f);
                    mat.SetInt("_SrcBlend", (int)BlendMode.One);
                    mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "Opaque");
                    mat.renderQueue = 2000;
                }

                result[info.Name] = mat;
            }
            return result;
        }

        public static GameObject Build(ObjPart part, Dictionary<string, Material> materials, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(part.Name);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();

            var mesh = new Mesh { name = part.Name };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(part.Vertices);
            mesh.SetUVs(0, part.Uvs);
            mesh.SetNormals(part.Normals);

            var keys = part.Tris.Keys.ToList();
            mesh.subMeshCount = keys.Count;

            var mats = new Material[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                mesh.SetTriangles(part.Tris[keys[i]], i);
                Material m;
                if (!materials.TryGetValue(keys[i], out m))
                    m = materials.Values.FirstOrDefault();
                mats[i] = m;
            }
            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var heat = go.AddComponent<GHPC.Thermals.HeatSource>();
            heat.heat = 0.7f;

            foreach (var m in mats)
            {
                if (m != null && m.shader != null && m.shader.name == "Standard")
                    continue;
            }

            return go;
        }
    }
}


















