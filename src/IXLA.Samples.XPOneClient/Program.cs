using Aida.Api.Client;

/*=================== CLI Exit Codes =================

    List of exit codes used in this sample.
    The program implements the required calls to the
    API to run full personalization of a document.

    In case of errors, the program terminates with
    corresponding error code.

    In contrast with other IXLA systems, the XP-One
    has no transport module, passports are loaded
    manually, for that reason commands like reset are
    not necessary.

    To reset the system to it's initial state
    operators should:

    1. Open the lid
    2. Remove the passport

======================================================*/

const int E_PERSO_BEGIN_FAILED = 1;
const int E_PERSO_END_FAILED = 2;
const int E_LOAD_LAYOUT_FAILED = 3;
const int E_UPDATE_LAYOUT_FAILED = 4;
const int E_AUTO_POS_FAILED = 5;
const int E_MARK_LAYOUT_FAILED = 6;

/*============== Input values ==========================

   Hardcoded values to keep the samples simple.

   We're defining:

   - Path of the required SJF files

   - Name of the auto-pos settings we want to use
     These are configured using the WebApp as usual

   - Path of bitmaps (the API needs the raw bytes here,
     we include the files to avoid bloating the samples
     with DB code etc.)

========================================================*/

var ASSETS_ROOT = Path.Combine("assets");

const string BASE_URL = "http://192.168.3.128:5000";

var PAGE_2_SJF_PATH = Path.Combine(ASSETS_ROOT, "Layouts", "PAGE_2.sjf"); // Load sjf for page 2
const string PAGE_2_AUTO_POS_NAME = "Datapage"; // name of the auto-pos we want to use for page 2
var PAGE_2_PHOTO_FILE_PATH = Path.Combine(ASSETS_ROOT, "PHOTO.bmp"); // Path of the portrait photo

var MLI_SJF_SJF_PATH = Path.Combine(ASSETS_ROOT, "Layouts", "PAGE_2_MLI.sjf"); // Same as above but for the MLI 

const string MLI_AUTO_POS_NAME =
    "Datapage_MLI"; // Having different SJF files is required if you want to use different auto-pos/camera settings 

var MLI_PHOTO_FILE_PATH = Path.Combine(ASSETS_ROOT, "MLI_PHOTO.bmp"); // path of the image used for MLI

/*=============== Client initialization ================

   The API Client requires just an HTTP Client instance
   and the BASE_URL of the API endpoints.

   The client was generated using a custom generator
   because it provides better developer ergonomics:

   - Support for enums
   - Support for flags
   - Typed HTTP Error responses

   Using the custom generator is not mandatory, you
   can use any tool/library of your preference to do
   the same.

   All errors are reported with corresponding HTTP
   status code, and typed responses.

========================================================*/

var client = new HttpClient(); //
var xpOneApi = new XpOneApi(client, new Uri(BASE_URL));

/*=============== Begin Personalization =================

    When we call this endpoint, the firmware makes
    sure the passport is properly docked and the
    door is closed.

    Bein/End Personalization change the lamp color
    and uses different blinking patterns to guide the
    operator on the action required

    If there are no open interlocks and the passport is
    loaded when the user presses the button (top-center)
    The LED becomes orange, indicating the laser is armed

=========================================================*/

try
{
    await xpOneApi.PersonalizationBeginAsync();
}
catch (ApiException e)
{
    /*
       The server verifies system state.
       returns forbidden (which resolves as an ApiException)
       If:
        - The machine is still initializing
        - No passport was loaded
        - User didn't press the button to enable the laser
    */
    TerminateProgram(E_PERSO_BEGIN_FAILED, e.Message);
}

/*============== prepare data + auto-pos ================

   The document is already docked inside the machine.
   We can compute the XY offsets for AutoPos and load
   data (layouts + updates) in parallel

=========================================================*/

Console.WriteLine("Load layouts + auto-pos calculation");

var loadLayoutsTask = LoadLayoutsAndUpdateEntitiesAsync(); // start loading data
var autoPosTask = ComputeAutoPosOffsetsAsync(); // compute offsets
await Task.WhenAll(loadLayoutsTask, autoPosTask); // wait completion/failure
var (autoPosPage2, autoPosMli) = await autoPosTask; // get results


/*============== Mark Layouts ===========================

   Offsets computed by the auto-pos api
   (XY registration) are sent as parameters to
   the mark layout API.

   The Y offset is inverted to match SAMLight
   coordinate system.

=========================================================*/

Console.WriteLine(
    $"""
     Marking layouts:
       PAGE_2:
          offset:
            x: {autoPosPage2.OffsetXMillimeters:3f}mm
            y: {-autoPosPage2.OffsetYMillimeters:3f}mm

       MLI_PAGE_2:
          offset:
            x: {autoPosMli.OffsetXMillimeters:3f}mm
            y: {-autoPosMli.OffsetYMillimeters:3f}mm
     """
);


try
{
    await xpOneApi.MarkLayoutXpOneAsync("PAGE_2", autoPosPage2.OffsetXMillimeters, -autoPosPage2.OffsetYMillimeters);
    Console.WriteLine("Mark layout PAGE_2 completed");
    
    await xpOneApi.MarkLayoutXpOneAsync("MLI_PAGE_2", autoPosMli.OffsetXMillimeters, -autoPosMli.OffsetYMillimeters);
    Console.WriteLine("Mark layout MLI_PAGE_2 completed");
}
catch (Exception e)
{
    TerminateProgram(E_MARK_LAYOUT_FAILED);
}

/* ============= End Personalization ====================

   Tells the machine that we won't be doing anything
   else with the document and can be safely removed.

   LEDs bink green in a "wave" pattern that indicates
   to open the lid and remove the passport.

=========================================================*/

await xpOneApi.PersonalizationEndAsync();

await xpOneApi.ToggleLampXpOneAsync();
Console.WriteLine("Personalization completed");

return;


/*=================== UTILITIES =========================

    These are just local functions to make the
    actual sequence of API calls more explicit

    Compute auto-pos offsets run invoke the API that

    LoadLayouts loads in SAMLight the layouts
    synchronously (SAMLight COM interface cannot execute commands in parallel).

=========================================================*/

async Task<(AutoPosResult Page, AutoPosResult Mli)> ComputeAutoPosOffsetsAsync()
{
    // execute auto-pos synchronously, since we might need different
    // camera settings to acquire the images from the camera.
    // Image acquisition presets are part of the auto-pos/template-matching
    // configuration which is done up-front 

    try
    {
        var resPage2 = await xpOneApi.ExecuteAutoPosXpOneAsync(PAGE_2_AUTO_POS_NAME);
        var resMli = await xpOneApi.ExecuteAutoPosXpOneAsync(MLI_AUTO_POS_NAME);

        return (resPage2, resMli);
    }
    catch (ApiException e)
    {
        TerminateProgram(E_AUTO_POS_FAILED, e.Message);

        // this is here just to make the compiler happy
        // temrinate program calls Environment.Exit
        return (null!, null!);
    }
}


// In this example, we load the layouts and 
// update entities, in real integrations we would 
// load the layouts once, and then call only UpdateEntitiesXpOne
// to avoid re-loading the SJF files every time.
async Task LoadLayoutsAndUpdateEntitiesAsync()
{
    try
    {
        // load layout for page 2
        await xpOneApi.LoadLayoutXpOneAsync(
            new LoadLayoutXpOneRequest {
                LayoutFile       = File.ReadAllBytes(PAGE_2_SJF_PATH), // binary content of the .sjf file
                LayoutName       = "PAGE_2", // name that can be later referenced by mark-layout
                OverrideEntities = true // NOTE: clears the SJF
            }
        );

        // load layout for MLI (page 2)
        // using 2 layouts instead of just one is required 
        // if we want to use different auto-pos settings 
        await xpOneApi.LoadLayoutXpOneAsync(
            new LoadLayoutXpOneRequest {
                LayoutFile       = File.ReadAllBytes(MLI_SJF_SJF_PATH),
                LayoutName       = "MLI_PAGE_2",
                OverrideEntities = false // NOTE: we use false this time, to avoid clearing entities loaded in previous call    
            }
        );
    }
    catch (ApiException e)
    {
        TerminateProgram(E_LOAD_LAYOUT_FAILED, e.Message);
    }


    try
    {
        // Update layout entities with personalization data
        await xpOneApi.UpdateEntitiesXpOneAsync(
            new UpdateEntitiesXpOneRequest {
                Assets = new List<FormFile>() {
                    new(File.ReadAllBytes(PAGE_2_PHOTO_FILE_PATH), "PHOTO"), // Images are transferred as multipart/form-data 
                    new(File.ReadAllBytes(MLI_PHOTO_FILE_PATH), "MLI_PHOTO"), // the client library handles it automatically
                },

                // In the data dictionary the keys are entity names (SAMLight) 
                // Text entities: string values, 
                // The ideal image resolution is 508dpi (1px ~ size of the laser spot).
                // If you use image formats that carry DPI metadata, make sure it's set to the proper value t
                // to avoid SAMLight scaling the images when loaded

                Data = new Dictionary<string, string>() {
                    ["PHOTO"]      = "@PHOTO", // The @ sign can be used to reference uploaded assets by name 
                    ["MLI_PHOTO"]  = "@MLI_PHOTO", // NOTE: Asset resolution via @ applies only to images
                    ["SOME_VALUE"] = "@CIRILLO", // Example of text entity starting with @
                    ["NAME"]       = "PABLO JULIAN", // Values for Text entities
                    ["SURNAME"]    = "CIRILLO", // --
                }
            }
        );
    }
    catch (ApiException e)
    {
        TerminateProgram(E_UPDATE_LAYOUT_FAILED, e.Message);
    }
}

// Utility method to print an error message and terminate 
// the program.
void TerminateProgram(int exitCode, string message = "")
{
    Console.Error.WriteLine(message);
    Environment.Exit(exitCode);
}