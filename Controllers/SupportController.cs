using Microsoft.AspNetCore.Mvc;
using TiendaOnline.Services.Agents;

namespace TiendaOnline.Controllers;

// Chat del agente de soporte (agente 3): responde fundamentado en el RAG de los SLA, con guardrails.
// GET /support -> formulario; POST /support/ask -> respuesta (JSON) con la seccion citada o el bloqueo.
[Route("support")]
public class SupportController(SupportAgent agent) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromForm] int productId, [FromForm] string pregunta, CancellationToken ct)
    {
        var r = await agent.AnswerAsync(productId, pregunta ?? "", ct: ct);
        // JSON estructurado: respuesta, fuentes (secciones citadas) y si lo bloqueo un guardrail.
        return Json(new
        {
            answered = r.Answered,
            respuesta = r.Text,
            fuentes = r.Sources,
            bloqueadoPor = r.BlockedBy,
        });
    }
}
