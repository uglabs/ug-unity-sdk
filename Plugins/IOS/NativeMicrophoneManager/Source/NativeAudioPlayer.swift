import Foundation
import AVFoundation

@objc public class NativeAudioPlayer: NSObject {
    @objc public static var shared: NativeAudioPlayer!
    private var playerNode: AVAudioPlayerNode?
    private var audioFormat: AVAudioFormat?
    private var isPlaying = false
    private var nextScheduledTime: AVAudioTime?
    
    @objc public static func createSharedInstance() {
        shared = NativeAudioPlayer()
    }
    
    @objc public func start(sampleRate: Double) {
        NSLog("NativeAudioPlayer: Starting with sample rate: \(sampleRate)")
        
        // Create audio format
        audioFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32,
                                  sampleRate: sampleRate,
                                  channels: 1,
                                  interleaved: false)
        
        NSLog("NativeAudioPlayer: Created audio format: \(String(describing: audioFormat))")
        
        // Create and configure player node
        playerNode = AVAudioPlayerNode()

        NSLog("NativeAudioPlayer: Created player node: \(String(describing: playerNode))")
        
        guard let playerNode = playerNode,
              let audioEngine = SharedAudioEngine.shared.audioEngine else {
            NSLog("NativeAudioPlayer: Failed to create player node or get audio engine")
            return
        }
        
        // Attach and connect the player node
        audioEngine.attach(playerNode)
        audioEngine.connect(playerNode, to: audioEngine.mainMixerNode, format: audioFormat)

        NSLog("NativeAudioPlayer: Attached and connected player node to audio engine")
        
        // Start the engine
        do {
            try SharedAudioEngine.shared.startEngine()
            isPlaying = true
            NSLog("NativeAudioPlayer: Player node attached and connected, engine started")
        } catch {
            NSLog("NativeAudioPlayer: Failed to start audio engine: \(error)")
            audioEngine.detach(playerNode)
            self.playerNode = nil
        }
    }
    
    @objc public func playBuffer(buffer: [Float]) {
        guard let playerNode = playerNode,
              let audioFormat = audioFormat,
              isPlaying else {
            NSLog("NativeAudioPlayer: Cannot play buffer - player not ready")
            return
        }

        let frameCount = AVAudioFrameCount(buffer.count)
        guard let audioBuffer = AVAudioPCMBuffer(pcmFormat: audioFormat, frameCapacity: frameCount) else {
            NSLog("NativeAudioPlayer: Failed to create PCM buffer")
            return
        }
        
        audioBuffer.frameLength = frameCount
        memcpy(audioBuffer.floatChannelData?[0], buffer, buffer.count * MemoryLayout<Float>.size)

        let bufferDuration = Double(frameCount) / audioFormat.sampleRate

        // Determine scheduling time
        if nextScheduledTime == nil {
            // First buffer: schedule ASAP based on player time
            if let lastRenderTime = playerNode.lastRenderTime,
               let playerTime = playerNode.playerTime(forNodeTime: lastRenderTime) {
                let sampleTime = playerTime.sampleTime + AVAudioFramePosition(buffer.count)
                nextScheduledTime = AVAudioTime(sampleTime: sampleTime, atRate: audioFormat.sampleRate)
            }
        }

        playerNode.scheduleBuffer(audioBuffer, at: nextScheduledTime, options: []) {
            // NSLog("NativeAudioPlayer: Buffer played")
        }

        // Increment for next buffer
        if let scheduledTime = nextScheduledTime {
            let nextSampleTime = scheduledTime.sampleTime + AVAudioFramePosition(buffer.count)
            nextScheduledTime = AVAudioTime(sampleTime: nextSampleTime, atRate: audioFormat.sampleRate)
        }

        if !playerNode.isPlaying {
            playerNode.play()
        }
    }
    
    @objc public func stop() {
        if let playerNode = playerNode,
           let audioEngine = SharedAudioEngine.shared.audioEngine {
            playerNode.stop()
            audioEngine.detach(playerNode)
        }
        playerNode = nil
        isPlaying = false
        SharedAudioEngine.shared.stopEngine()
    }
}