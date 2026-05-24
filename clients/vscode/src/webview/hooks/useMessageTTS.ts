export function useMessageTTS(): { speak: () => void; stop: () => void; isSpeaking: boolean } {
	return { speak: () => {}, stop: () => {}, isSpeaking: false };
}
