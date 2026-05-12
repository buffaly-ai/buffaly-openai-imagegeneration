class ImageGenerationHarnessJsonWsServiceService {
  constructor({ baseUrl = "/api/buffaly.openai.imagegeneration.webharness/image-generation-harness-json-ws-service", authToken = null } = {}) {
    this.Url = baseUrl;
    this.AuthToken = authToken;
  }
  _validate(oObject, validatorSchema, onValidationErrorCallback) {
    if (oObject == null) oObject = {};
    if (oObject.IsValidated == null || !oObject.IsValidated) {
      if (!Validators.Validate(oObject, validatorSchema)) {
        var oError = { Error: "Invalid data", Data: oObject };
        if (onValidationErrorCallback != null) onValidationErrorCallback(oError);
        else if (Page.HandleValidationErrors) Page.HandleValidationErrors(oError);
        throw "Invalid data";
      }
    }
  }
  GetConfig(request, Callback) { return this._invokeWrap("get-config", "GetConfig", request, ImageGenerationHarnessJsonWsServiceValidators.GetConfig, Callback); }
  async GetConfigAsync(request) { return await ObjectUtil.Promisify(this, this.GetConfig, [request]); }
  GenerateImage(request, Callback) { return this._invokeWrap("generate-image", "GenerateImage", request, ImageGenerationHarnessJsonWsServiceValidators.GenerateImage, Callback); }
  async GenerateImageAsync(request) { return await ObjectUtil.Promisify(this, this.GenerateImage, [request]); }
  EditImage(request, Callback) { return this._invokeWrap("edit-image", "EditImage", request, ImageGenerationHarnessJsonWsServiceValidators.EditImage, Callback); }
  async EditImageAsync(request) { return await ObjectUtil.Promisify(this, this.EditImage, [request]); }
  ListOutputs(request, Callback) { return this._invokeWrap("list-outputs", "ListOutputs", request, ImageGenerationHarnessJsonWsServiceValidators.ListOutputs, Callback); }
  async ListOutputsAsync(request) { return await ObjectUtil.Promisify(this, this.ListOutputs, [request]); }
  _invokeWrap(kebab, methodName, request, validator, Callback) {
    this._validate(request, validator, null);
    return this._invoke(this.Url + "/" + kebab, methodName, { request: request }, { Serialize: {} }, Callback);
  }
  _invoke(pageUrl, methodName, params, methodConfig, Callback) {
    var initializer = { Page: pageUrl, Method: methodName, Params: params, Serialize: methodConfig.Serialize || {}, onDataReceived: Callback ? function(oRes) { Callback(oRes); } : null, onErrorReceived: (Page.HandleUnexpectedError ? Page.HandleUnexpectedError : null) };
    if (this.AuthToken) initializer.AuthToken = this.AuthToken;
    if (Callback) JsonMethod.callWithInitializer(initializer); else return JsonMethod.callSync(pageUrl, methodName, params, methodConfig.Serialize || {});
  }
}
var ImageGenerationHarnessJsonWsServiceValidators = {
  GetConfig: { RootDirectory:{Validators:[Validators.Text],InvalidMessage:"Invalid RootDirectory",IsOptional:true}, FileName:{Validators:[Validators.Text],InvalidMessage:"Invalid FileName",IsOptional:true} },
  ListOutputs: { RootDirectory:{Validators:[Validators.Text],InvalidMessage:"Invalid RootDirectory",IsOptional:true}, FileName:{Validators:[Validators.Text],InvalidMessage:"Invalid FileName",IsOptional:true} },
  GenerateImage: { RootDirectory:{Validators:[Validators.Text],InvalidMessage:"Invalid RootDirectory",IsOptional:true}, FileName:{Validators:[Validators.Text],InvalidMessage:"Invalid FileName",IsOptional:true}, Prompt:{Validators:[Validators.Text],InvalidMessage:"Invalid Prompt"}, Model:{Validators:[Validators.Text],InvalidMessage:"Invalid Model"}, Size:{Validators:[Validators.Text],InvalidMessage:"Invalid Size"}, Quality:{Validators:[Validators.Text],InvalidMessage:"Invalid Quality"}, Background:{Validators:[Validators.Text],InvalidMessage:"Invalid Background"}, OutputFormat:{Validators:[Validators.Text],InvalidMessage:"Invalid OutputFormat"}, OutputCompression:{Validators:[Validators.Text],InvalidMessage:"Invalid OutputCompression",IsOptional:true} },
  EditImage: { RootDirectory:{Validators:[Validators.Text],InvalidMessage:"Invalid RootDirectory",IsOptional:true}, FileName:{Validators:[Validators.Text],InvalidMessage:"Invalid FileName",IsOptional:true}, Prompt:{Validators:[Validators.Text],InvalidMessage:"Invalid Prompt"}, Model:{Validators:[Validators.Text],InvalidMessage:"Invalid Model"}, Size:{Validators:[Validators.Text],InvalidMessage:"Invalid Size"}, Quality:{Validators:[Validators.Text],InvalidMessage:"Invalid Quality"}, Background:{Validators:[Validators.Text],InvalidMessage:"Invalid Background"}, OutputFormat:{Validators:[Validators.Text],InvalidMessage:"Invalid OutputFormat"}, OutputCompression:{Validators:[Validators.Text],InvalidMessage:"Invalid OutputCompression",IsOptional:true}, Images:{Validators:[Validators.Array([Validators.Object])],InvalidMessage:"Invalid Images"}, Mask:{Validators:[Validators.Object],InvalidMessage:"Invalid Mask",IsOptional:true} }
};
if (typeof ImageGenerationHarnessJsonWsService === "undefined") { var ImageGenerationHarnessJsonWsService = new ImageGenerationHarnessJsonWsServiceService(); }
