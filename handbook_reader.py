import azure.functions as func
import json

app = func.FunctionApp()

@app.route(route="chat", auth_level=func.AuthLevel.FUNCTION)
def chat(req: func.HttpRequest) -> func.HttpResponse:

    body = req.get_json()
    question = body.get("question")

    return func.HttpResponse(
        json.dumps(
            {
                "question": question,
                "message": "Function is working"
            }
        ),
        mimetype="application/json",
        status_code=200
    )