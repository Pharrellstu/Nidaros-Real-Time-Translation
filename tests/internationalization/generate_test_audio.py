#!/usr/bin/env python3
"""
Generate synthetic TTS audio for internationalization testing.
Creates test audio files with Dutch and Arabic content for mixed-language testing.

Requirements:
    pip install gTTS pydub
"""

import os
from gtts import gTTS
from pydub import AudioSegment
from pydub.generators import Sine

def create_silence(duration_ms):
    """Create silence segment."""
    return AudioSegment.silent(duration=duration_ms)

def create_tone(duration_ms, frequency=440):
    """Create a tone to mark section transitions."""
    return Sine(frequency).to_audio_segment(duration=duration_ms)

def generate_dutch_arabic_test_audio():
    """
    Generate test audio with mixed Dutch and Arabic content.
    Simulates a church service with embedded Arabic prayers.
    """
    print("🎤 Generating Dutch + Arabic test audio...")
    
    # Test script content
    dutch_intro = "Welkom allemaal. Vandaag gaan we bidden."
    arabic_prayer = "بِسْمِ اللهِ الرَّحْمٰنِ الرَّحِيْمِ"  # Bismillah
    dutch_outro = "En nu verder in het Nederlands. Dit is een test van de vertaling."
    
    # Generate TTS for each segment
    print("  ├─ Generating Dutch intro...")
    tts_dutch_1 = gTTS(text=dutch_intro, lang='nl', slow=False)
    tts_dutch_1.save('temp_dutch_1.mp3')
    
    print("  ├─ Generating Arabic prayer...")
    tts_arabic = gTTS(text=arabic_prayer, lang='ar', slow=False)
    tts_arabic.save('temp_arabic.mp3')
    
    print("  ├─ Generating Dutch outro...")
    tts_dutch_2 = gTTS(text=dutch_outro, lang='nl', slow=False)
    tts_dutch_2.save('temp_dutch_2.mp3')
    
    # Load audio segments
    dutch_1 = AudioSegment.from_mp3('temp_dutch_1.mp3')
    arabic = AudioSegment.from_mp3('temp_arabic.mp3')
    dutch_2 = AudioSegment.from_mp3('temp_dutch_2.mp3')
    
    # Create transition tone
    tone = create_tone(500, frequency=880)  # 0.5s tone at 880 Hz
    
    # Combine segments with pauses
    combined = (
        dutch_1 + 
        create_silence(1000) +  # 1s pause
        tone +                   # Transition marker
        create_silence(500) +    # 0.5s pause
        arabic + 
        create_silence(1000) +   # 1s pause
        tone +                   # Transition marker
        create_silence(500) +    # 0.5s pause
        dutch_2
    )
    
    # Add 2 seconds of silence at the end for buffer
    combined = combined + create_silence(2000)
    
    # Export final audio
    output_path = 'audio/test_mixed_dutch_arabic.mp3'
    combined.export(output_path, format='mp3', bitrate='128k')
    print(f"  ✅ Created: {output_path} ({len(combined)/1000:.1f}s)")
    
    # Cleanup temp files
    for temp in ['temp_dutch_1.mp3', 'temp_arabic.mp3', 'temp_dutch_2.mp3']:
        if os.path.exists(temp):
            os.remove(temp)
    
    return output_path

def generate_arabic_only_test_audio():
    """Generate Arabic-only test audio for RTL validation."""
    print("🎤 Generating Arabic-only test audio...")
    
    # Pure Arabic content with numbers and punctuation
    arabic_text = "مرحبا بكم في عام ٢٠٢٦. السلام عليكم! كيف حالك؟"
    
    tts = gTTS(text=arabic_text, lang='ar', slow=False)
    output_path = 'audio/test_arabic_only.mp3'
    tts.save(output_path)
    
    # Add silence padding
    audio = AudioSegment.from_mp3(output_path)
    audio = audio + create_silence(2000)
    audio.export(output_path, format='mp3', bitrate='128k')
    
    print(f"  ✅ Created: {output_path} ({len(audio)/1000:.1f}s)")
    return output_path

def generate_arabic_with_english_names():
    """Generate Arabic text with embedded English names."""
    print("🎤 Generating Arabic + English names test audio...")
    
    # Arabic with English name (mixed BiDi text)
    arabic_text = "مرحبا مع John Smith و Mary Johnson في القاهرة"
    
    tts = gTTS(text=arabic_text, lang='ar', slow=False)
    output_path = 'audio/test_arabic_english_names.mp3'
    tts.save(output_path)
    
    # Add silence padding
    audio = AudioSegment.from_mp3(output_path)
    audio = audio + create_silence(2000)
    audio.export(output_path, format='mp3', bitrate='128k')
    
    print(f"  ✅ Created: {output_path} ({len(audio)/1000:.1f}s)")
    return output_path

def generate_encoding_stress_test():
    """Generate multilingual encoding stress test."""
    print("🎤 Generating encoding stress test audio...")
    
    # Multiple languages with diacritics and special characters
    segments = [
        ("Café in Paris", 'fr'),
        ("القاهرة في مصر", 'ar'),
        ("αγάπη στην Ελλάδα", 'el'),
        ("Здравствуйте из России", 'ru'),
    ]
    
    combined = AudioSegment.empty()
    
    for i, (text, lang) in enumerate(segments):
        print(f"  ├─ Generating segment {i+1}/4: {lang}")
        tts = gTTS(text=text, lang=lang, slow=False)
        temp_file = f'temp_encoding_{i}.mp3'
        tts.save(temp_file)
        
        segment = AudioSegment.from_mp3(temp_file)
        combined += segment + create_silence(1000)
        
        os.remove(temp_file)
    
    output_path = 'audio/test_encoding_stress.mp3'
    combined.export(output_path, format='mp3', bitrate='128k')
    
    print(f"  ✅ Created: {output_path} ({len(combined)/1000:.1f}s)")
    return output_path

def main():
    """Generate all test audio files."""
    print("=" * 60)
    print("🌍 Internationalization Test Audio Generator")
    print("=" * 60)
    
    # Ensure audio directory exists
    os.makedirs('audio', exist_ok=True)
    
    try:
        # Generate all test files
        files = []
        files.append(generate_dutch_arabic_test_audio())
        files.append(generate_arabic_only_test_audio())
        files.append(generate_arabic_with_english_names())
        files.append(generate_encoding_stress_test())
        
        print("\n" + "=" * 60)
        print("✅ Test Audio Generation Complete!")
        print("=" * 60)
        print("\nGenerated files:")
        for f in files:
            print(f"  • {f}")
        
        print("\n📝 Usage:")
        print("  1. Use FFmpeg to stream these files to Wowza:")
        print("     ffmpeg -re -i audio/test_mixed_dutch_arabic.mp3 \\")
        print("            -f flv rtmp://localhost:1935/whisper/testStream")
        print("\n  2. Or configure OBS to play these files as audio sources")
        
    except Exception as e:
        print(f"\n❌ Error: {e}")
        print("\n💡 Make sure you have installed dependencies:")
        print("   pip install gTTS pydub")
        return 1
    
    return 0

if __name__ == "__main__":
    exit(main())
