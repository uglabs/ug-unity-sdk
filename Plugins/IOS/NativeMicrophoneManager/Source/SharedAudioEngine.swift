import Foundation
import AVFoundation

// This is the audio engine that is shared between the NativeMicrophoneManager and the NativeAudioPlayer
// AEC does not work if we use the "streaming" audio from Unity - as it is likely that it creates a new audio engine internally
@objc public class SharedAudioEngine: NSObject {
    @objc public static let shared = SharedAudioEngine()
    
    private(set) var audioEngine: AVAudioEngine?
    private let audioSession = AVAudioSession.sharedInstance()
    private var activeUsers = 0
    private let engineLock = NSLock()
    
    private override init() {
        super.init()
        audioEngine = AVAudioEngine()
    }
    
    @objc public func configureAudioSession(enableAEC: Bool) throws {
        /*
         if enableAEC {
             try audioSession.setCategory(.playAndRecord, mode: .videoChat, options: [.allowBluetooth, .allowBluetoothA2DP])
         } else {
             try audioSession.setCategory(.playAndRecord, mode: .default, options: [.allowBluetooth, .allowBluetoothA2DP])
         }
         try audioSession.setActive(true)
         */
        // Configure audio session first
        try audioSession.setCategory(.playAndRecord, 
                                   mode: .videoChat, 
                                   options: [.defaultToSpeaker, .allowBluetooth, .allowBluetoothA2DP])
        try audioSession.setActive(true, options: .notifyOthersOnDeactivation)
        
        NSLog("SharedAudioEngine: Audio session configured - category: \(audioSession.category), mode: \(audioSession.mode)")
    }
    
    @objc public func startEngine() throws {
        engineLock.lock()
        defer { engineLock.unlock() }
        
        guard let audioEngine = audioEngine else {
            throw NSError(domain: "SharedAudioEngine", code: -1, userInfo: [NSLocalizedDescriptionKey: "Audio engine is nil"])
        }
        
        activeUsers += 1
        
        if !audioEngine.isRunning {
            NSLog("SharedAudioEngine: Preparing audio engine")
            audioEngine.prepare()
            
            NSLog("SharedAudioEngine: Starting audio engine (active users: \(activeUsers))")
            try audioEngine.start()
        }
    }
    
    @objc public func stopEngine() {
        engineLock.lock()
        defer { engineLock.unlock() }
        
        activeUsers -= 1
        
        if activeUsers <= 0 {
            NSLog("SharedAudioEngine: Stopping audio engine (no active users)")
            audioEngine?.stop()
            activeUsers = 0
        } else {
            NSLog("SharedAudioEngine: Not stopping engine (active users: \(activeUsers))")
        }
    }
    
    @objc public func checkAudioSessionConfiguration() {
        NSLog("Current audio session configuration:")
        NSLog("Category: \(audioSession.category)")
        NSLog("Mode: \(audioSession.mode)")
        NSLog("Options: \(audioSession.categoryOptions)")
        NSLog("Sample Rate: \(audioSession.sampleRate)")
        NSLog("IO Buffer Duration: \(audioSession.ioBufferDuration)")
        NSLog("Engine running: \(audioEngine?.isRunning ?? false)")
        NSLog("Active users: \(activeUsers)")
        NSLog("Attached nodes: \(audioEngine?.attachedNodes.count ?? 0)")
    }
} 
