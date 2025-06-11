module WebhookHandler

open Microsoft.AspNetCore.Http
open Giraffe
open System.Text.Json
open System.Net.Http
open System.Collections.Concurrent
open System.Threading.Tasks
open System

type PaymentPayload = {
    event: string
    transaction_id: string
    amount: float
    currency: string
    timestamp: string
}

let tokenEsperado = "meu-token-secreto"
let transacoesRecebidas = ConcurrentDictionary<string, bool>()

let confirmarTransacao (payload: PaymentPayload) =
    task {
        use client = new HttpClient()
        let json = JsonSerializer.Serialize(payload)
        use content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        let! _ = client.PostAsync("http://localhost:5000/confirmar", content)
        return ()
    }

let cancelarTransacao (payload: PaymentPayload) =
    task {
        use client = new HttpClient()
        let json = JsonSerializer.Serialize(payload)
        use content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        let! _ = client.PostAsync("http://localhost:5000/cancelar", content)
        return ()
    }

let webhookHandler: HttpHandler =
    fun next ctx -> task {
        let token = ctx.Request.Headers.["X-Webhook-Token"].ToString()

        if token <> tokenEsperado then
            let unauthorizedHandler = RequestErrors.UNAUTHORIZED "Bearer" "Webhook" "Token inválido"
            return! unauthorizedHandler next ctx
        else
            let! payloadResult =
                task {
                    try
                        let! p = ctx.BindJsonAsync<PaymentPayload>()
                        return Ok p
                    with ex ->
                        return Error $"Erro ao desserializar payload: {ex.Message}"
                }

            match payloadResult with
            | Ok payload ->
                if System.String.IsNullOrWhiteSpace(payload.transaction_id) then
                    let! result = RequestErrors.badRequest (text "transaction_id obrigatório") next ctx
                    return result
                else if transacoesRecebidas.ContainsKey(payload.transaction_id) then
                    let! result = RequestErrors.badRequest (text "Transação duplicada") next ctx
                    return result
                else if payload.amount <= 0.0 || payload.currency <> "BRL" then
                    do! cancelarTransacao payload // Side effect: await cancellation
                    let! result = RequestErrors.badRequest (text "Dados inválidos") next ctx
                    return result
                else if System.String.IsNullOrWhiteSpace(payload.timestamp) then
                    do! cancelarTransacao payload // Side effect: await cancellation
                    let! result = RequestErrors.badRequest (text "Timestamp ausente") next ctx
                    return result
                else
                    transacoesRecebidas.TryAdd(payload.transaction_id, true) |> ignore
                    do! confirmarTransacao payload

                    let! result = Successful.OK (text "Transação confirmada") next ctx
                    return result

            | Error errorMessage ->
                let! result = RequestErrors.badRequest (text errorMessage) next ctx
                return result
    }