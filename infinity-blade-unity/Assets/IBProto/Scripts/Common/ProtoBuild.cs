using UnityEngine;
using UnityEngine.Rendering;

// 基本図形とマテリアルをコードで作るための道具
public static class ProtoBuild
{
    static bool IsURP => GraphicsSettings.currentRenderPipeline != null;

    public static Material Lit(Color c)
    {
        Shader s = IsURP ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
        if (s == null) s = Shader.Find("Standard");
        var m = new Material(s);
        SetColor(m, c);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    public static Material Unlit(Color c)
    {
        Shader s = IsURP ? Shader.Find("Universal Render Pipeline/Unlit") : Shader.Find("Unlit/Color");
        if (s == null) s = Shader.Find("Unlit/Color");
        var m = new Material(s);
        SetColor(m, c);
        return m;
    }

    public static Material LineMat()
    {
        return new Material(Shader.Find("Sprites/Default"));
    }

    public static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    public static GameObject Box(string name, Vector3 size, Vector3 pos, Material mat, Transform parent, bool collider = true)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.Destroy(g.GetComponent<Collider>());
        return g;
    }

    // 上向き(+Y)の円錐。原点が中心
    public static GameObject Cone(string name, float radius, float height, int seg, Vector3 pos, Vector3 eulerDeg, Material mat, Transform parent)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(eulerDeg);
        var mesh = new Mesh();
        var v = new Vector3[seg * 3 + seg * 3];
        var t = new int[seg * 6];
        Vector3 tip = new Vector3(0, height / 2f, 0), center = new Vector3(0, -height / 2f, 0);
        int vi = 0, ti = 0;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.PI * 2f, a1 = (i + 1) / (float)seg * Mathf.PI * 2f;
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, -height / 2f, Mathf.Sin(a0) * radius);
            Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, -height / 2f, Mathf.Sin(a1) * radius);
            // 側面
            v[vi] = tip; v[vi + 1] = p1; v[vi + 2] = p0;
            t[ti++] = vi; t[ti++] = vi + 1; t[ti++] = vi + 2; vi += 3;
            // 底面
            v[vi] = center; v[vi + 1] = p0; v[vi + 2] = p1;
            t[ti++] = vi; t[ti++] = vi + 1; t[ti++] = vi + 2; vi += 3;
        }
        mesh.vertices = v; mesh.triangles = t; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true;
        return g;
    }
}
