// Barra de vida sobre la cabeza de las criaturas. Survivalcraft API 1.9.
// Un solo archivo (igual que Zombies.cs): se compila en el mismo dll o en uno propio.
//  - Solo se muestra si la criatura tiene la vida por debajo del maximo.
//  - Verde -> amarillo -> rojo segun la vida. Se oculta lejos y detras de bloques.
using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Media;
using GameEntitySystem;

namespace Game
{
    public class HealthBarsLoader : ModLoader
    {
        // ---- ajustes: cambia estos numeros a tu gusto ----
        const float Distancia = 15f;        // bloques; mas lejos no se dibuja
        const float AnchoMinimo = 0.9f;     // ancho minimo de la barra (bloques)
        const float Alto = 0.11f;           // grosor de la barra
        const float Separacion = 0.35f;     // altura sobre la cabeza
        const bool MostrarSiVidaLlena = true; 
        const bool MostrarAlJugador = false;
        const byte AlfaNormal = 128;        // 50% transparente
        const float SegundosOpaca = 3f;     // tras un golpe, 100% visible este tiempo
        const bool MostrarNumeros = true;   // vida en numeros: 15/25
        const float TamanoTexto = 0.011f;   // tamano de los numeros

        static readonly PrimitivesRenderer3D renderizador = new PrimitivesRenderer3D();
        SubsystemCreatureSpawn creaturas;
        static BitmapFont fuente;
        readonly Dictionary<ComponentCreature, float> vidaPrevia = new Dictionary<ComponentCreature, float>();
        readonly Dictionary<ComponentCreature, double> ultimoGolpe = new Dictionary<ComponentCreature, double>();
        double proximaLimpieza;

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("InitializeCreatureTypes", this);
            ModsManager.RegisterHook("GuiDraw", this);
        }

        // el juego llama a este hook una vez por cada mundo que se carga
        public override void InitializeCreatureTypes(
            SubsystemCreatureSpawn subsystemCreatureSpawn,
            List<SubsystemCreatureSpawn.CreatureType> creatureTypes)
        {
            creaturas = subsystemCreatureSpawn;
            vidaPrevia.Clear();
            ultimoGolpe.Clear();
        }

        public override void GuiDraw(ComponentGui componentGui, Camera camera, int drawOrder)
        {
            if (creaturas == null || camera == null) return;

            Matrix vista = camera.ViewMatrix;
            Vector3 derecha = new Vector3(vista.M11, vista.M21, vista.M31);
            Vector3 arriba = new Vector3(vista.M12, vista.M22, vista.M32);
            Vector3 ojo = camera.ViewPosition;

            FlatBatch3D lote = renderizador.FlatBatch(
                100, DepthStencilState.DepthRead, RasterizerState.CullNone, BlendState.AlphaBlend);

            FontBatch3D texto = null;
            if (MostrarNumeros)
            {
                if (fuente == null) fuente = ContentManager.Get<BitmapFont>("Fonts/Pericles");
                texto = renderizador.FontBatch(
                    fuente, 101, DepthStencilState.DepthRead, RasterizerState.CullNone,
                    BlendState.AlphaBlend, SamplerState.LinearClamp);
            }

            double ahora = Time.RealTime;
            if (ahora > proximaLimpieza)
            {
                proximaLimpieza = ahora + 10.0;
                List<ComponentCreature> quitar = new List<ComponentCreature>();
                foreach (ComponentCreature k in vidaPrevia.Keys)
                    if (k.ComponentHealth == null || k.ComponentHealth.Health <= 0f) quitar.Add(k);
                foreach (ComponentCreature k in quitar) { vidaPrevia.Remove(k); ultimoGolpe.Remove(k); }
            }

            bool algo = false;
            foreach (ComponentCreature c in creaturas.Creatures)
            {
                if (c == null || c.ComponentBody == null || c.ComponentHealth == null) continue;
                float vida = c.ComponentHealth.Health;
                if (vida <= 0f) continue;
                if (!MostrarSiVidaLlena && vida >= 0.999f) continue;
                if (!MostrarAlJugador && c.Entity.FindComponent<ComponentPlayer>(false) != null) continue;

                float anterior;
                if (vidaPrevia.TryGetValue(c, out anterior) && vida < anterior - 0.0001f) ultimoGolpe[c] = ahora;
                vidaPrevia[c] = vida;
                double golpe;
                bool recienGolpeada = ultimoGolpe.TryGetValue(c, out golpe) && ahora - golpe < SegundosOpaca;
                byte alfa = recienGolpeada ? (byte)255 : AlfaNormal;

                ComponentBody cuerpo = c.ComponentBody;
                Vector3 centro = cuerpo.Position + new Vector3(0f, cuerpo.BoxSize.Y + Separacion, 0f);
                if (Vector3.Distance(centro, ojo) > Distancia) continue;

                float ancho = Math.Max(AnchoMinimo, cuerpo.BoxSize.X * 1.2f);
                float mitad = ancho * 0.5f;
                float v = MathUtils.Clamp(vida, 0f, 1f);
                float h = Alto * 0.5f;

                // fondo oscuro
                Cuadro(lote, centro, derecha, arriba, -mitad - 0.02f, mitad + 0.02f, -h - 0.02f, h + 0.02f,
                    new Color((byte)0, (byte)0, (byte)0, (byte)(170 * alfa / 255)));
                // relleno
                Cuadro(lote, centro, derecha, arriba, -mitad, -mitad + ancho * v, -h, h, ColorVida(v, alfa));
                if (texto != null)
                {
                    float max = c.ComponentHealth.AttackResilience;
                    string etiqueta = max > 0f
                        ? ((int)Math.Ceiling(v * max)).ToString() + "/" + ((int)Math.Round(max)).ToString()
                        : ((int)Math.Ceiling(v * 100f)).ToString() + "%";
                    texto.QueueText(etiqueta, centro + arriba * (h + 0.05f),
                        derecha * TamanoTexto, -arriba * TamanoTexto, new Color((byte)255, (byte)255, (byte)255, alfa),
                        TextAnchor.HorizontalCenter | TextAnchor.Bottom);
                }
                algo = true;
            }

            if (algo) renderizador.Flush(camera.ViewProjectionMatrix);
        }

        static void Cuadro(FlatBatch3D lote, Vector3 centro, Vector3 derecha, Vector3 arriba,
            float x0, float x1, float y0, float y1, Color color)
        {
            Vector3 a = centro + derecha * x0 + arriba * y0;
            Vector3 b = centro + derecha * x1 + arriba * y0;
            Vector3 c = centro + derecha * x1 + arriba * y1;
            Vector3 d = centro + derecha * x0 + arriba * y1;
            lote.QueueQuad(a, b, c, d, color);
        }

        static Color ColorVida(float v, byte alfa)
        {
            // 1 = verde, 0.5 = amarillo, 0 = rojo
            byte r = (byte)(255f * MathUtils.Clamp(2f - 2f * v, 0f, 1f));
            byte g = (byte)(255f * MathUtils.Clamp(2f * v, 0f, 1f));
            return new Color(r, g, (byte)30, alfa);
        }
    }
}
