﻿using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI.ObjectModels;

namespace Thor.Abstractions.ObjectModels.ObjectModels.RequestModels;

/// <summary>
///     Definition of a valid tool.
/// </summary>
public class ToolDefinition
{
    /// <summary>
    ///     Required. The type of the tool. Currently, only function is supported.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }


    /// <summary>
    ///     A list of functions the model may generate JSON inputs for.
    /// </summary>
    [JsonIgnore]
    public FunctionDefinition? Function { get; set; }

    [JsonIgnore] public object? FunctionsAsObject { get; set; }

    /// <summary>
    ///     Required. The description of what the function does.
    /// </summary>
    [JsonPropertyName("function")]
    public object? FunctionCalculated
    {
        get
        {
            if (FunctionsAsObject != null && Function != null)
            {
                throw new ValidationException(
                    "FunctionAsObject and Function can not be assigned at the same time. One of them is should be null.");
            }

            return Function ?? FunctionsAsObject;
        }
        set
        {
            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Object)
                    Function = JsonSerializer.Deserialize<FunctionDefinition>(jsonElement.GetRawText(),
                        ThorJsonSerializer.DefaultOptions);
                else
                {
                    FunctionsAsObject = jsonElement;
                }
            }
        }
    }

    public static ToolDefinition DefineFunction(FunctionDefinition function) => new()
    {
        Type = StaticValues.CompletionStatics.ToolType.Function,
        Function = function
    };
}