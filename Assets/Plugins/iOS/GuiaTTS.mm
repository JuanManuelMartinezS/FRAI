// Voz offline en iPhone con AVSpeechSynthesizer (motor de voz del sistema).
#import <AVFoundation/AVFoundation.h>

static AVSpeechSynthesizer *guiaSynth = nil;

extern "C" {

void GuiaTTS_Hablar(const char *texto, const char *idioma)
{
    if (texto == NULL) return;
    if (guiaSynth == nil) guiaSynth = [[AVSpeechSynthesizer alloc] init];

    // Permite que suene aunque el celular este en silencio y junto con la camara.
    [[AVAudioSession sharedInstance] setCategory:AVAudioSessionCategoryPlayback
                                     withOptions:AVAudioSessionCategoryOptionMixWithOthers error:nil];
    [[AVAudioSession sharedInstance] setActive:YES error:nil];

    NSString *t = [NSString stringWithUTF8String:texto];
    NSString *lang = idioma ? [NSString stringWithUTF8String:idioma] : @"es-CO";
    AVSpeechUtterance *u = [AVSpeechUtterance speechUtteranceWithString:t];
    AVSpeechSynthesisVoice *voz = [AVSpeechSynthesisVoice voiceWithLanguage:lang];
    if (voz == nil) voz = [AVSpeechSynthesisVoice voiceWithLanguage:@"es-MX"];
    if (voz == nil) voz = [AVSpeechSynthesisVoice voiceWithLanguage:@"es-ES"];
    u.voice = voz;
    u.rate = AVSpeechUtteranceDefaultSpeechRate * 0.95f;

    [guiaSynth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
    [guiaSynth speakUtterance:u];
}

void GuiaTTS_Detener(void)
{
    if (guiaSynth) [guiaSynth stopSpeakingAtBoundary:AVSpeechBoundaryImmediate];
}

bool GuiaTTS_Hablando(void)
{
    return guiaSynth != nil && guiaSynth.isSpeaking;
}

}
