import Foundation
import AVFoundation

@objc public class NativeMicrophoneManager: NSObject {
    @objc public static var shared: NativeMicrophoneManager!

    private var audioEngine: AVAudioEngine?
    private let audioSession = AVAudioSession.sharedInstance()
    private var circularAudioBuffer: [Float]
    private var writePosition = 0
    private var isRecording = false
    private let desiredSampleRate: Double
    private let recordingQueue: DispatchQueue
    private var previousRoute: String?

    @objc private init(sampleRate: Double, bufferLengthSeconds: Int) {
        self.desiredSampleRate = sampleRate
        let circularBufferLength = Int(desiredSampleRate) * bufferLengthSeconds
        self.circularAudioBuffer = [Float](repeating: 0, count: circularBufferLength)
        self.recordingQueue = DispatchQueue(label: "AudioRecorderQueue")
        super.init()
        NSLog("Initialized native audio manager.")
    }

    @objc public func initializeAEC() -> Bool {
        NSLog("Init basic AEC - native")
        let audioSession = AVAudioSession.sharedInstance()

        do {
            if #available(iOS 18.2, *), audioSession.isEchoCancelledInputAvailable { // This enables echo cancellation explicitly
                //!!!!!!!!!!!!!!!! try audioSession.setPrefersEchoCancelledInput(true)
                NSLog("Echo cancellation is enabled with setPrefersEchoCancelledInput")
            } else { // This enables echo cancellation implicityly through the playAndRecord setting
                // Use mode: .default for unprocessed, try .videoChat for native AEC (not great)
                //!!!!!!!!!!!!!!!! try audioSession.setCategory(.playAndRecord, mode: .voiceChat, options: [.allowBluetooth, .allowBluetoothA2DP])
                try audioSession.setCategory(.playAndRecord, mode: .default, options: [.allowBluetooth, .allowBluetoothA2DP])
                // try audioSession.setPreferredSampleRate(44000)
                // try audioSession.setInputGain(0.4) this doesn't work on most ios devices
               try audioSession.setActive(true)
                NSLog("Echo cancellation is enabled with playAndRecord")
            }
        } catch {
            NSLog("Failed to set up echo cancellation: \(error)")
            return false;
        }

        return true;
    }


    @objc public static func createSharedInstance(sampleRate: Double, bufferLengthSeconds: Int) {
        shared = NativeMicrophoneManager(sampleRate: sampleRate, bufferLengthSeconds: bufferLengthSeconds)
    }

    @objc public func isRecordPermissionGranted() -> Bool {
        if #available(iOS 17.0, *) {
            return AVAudioApplication.shared.recordPermission == .granted
        } else {
            // Fallback on earlier versions
            return AVAudioSession.sharedInstance().recordPermission == .granted
        }
    }

    @objc public func requestPermissionThenStart() {
        if #available(iOS 17.0, *) {
            AVAudioApplication.requestRecordPermission { granted in
                // This closure executes asynchronously on completion
                if granted {
                    // UnitySendMessage("Tests", "StartRecording", "")
                } else {
                    NSLog("Microphone permission denied")
                }
            }
        } else {
            // Fallback on earlier versions
            AVAudioSession.sharedInstance().requestRecordPermission { [weak self] granted in
                if granted {
                    // UnitySendMessage("Tests", "StartRecording", "")
                } else {
                    NSLog("Audio permission not granted.")
                }
            }
        }
    }

    @objc public func start(enableAEC: Bool) {
        if !isRecordPermissionGranted() {
            NSLog("Audio permission not granted. Failed to start recording.")
            return
        }

        guard !isRecording else {
            NSLog("Already recording.")
            return
        }

        do {
            // Configure the audio session
            // try configureAudioSession(enableAEC: false) //enableAEC) //! Disabling native AEC by default!!
            self.audioEngine = nil
            self.audioEngine = AVAudioEngine()
            
            guard let audioEngine = self.audioEngine else {
                NSLog("Audio engine is nil.")
                return
            }

            let inputNode = audioEngine.inputNode
            let inputFormat = inputNode.outputFormat(forBus: 0)
            
            guard let outputFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: self.desiredSampleRate, channels: 1, interleaved: false) else {
                fatalError("Failed to create output format.")
            }

            // Initialize the audio converter
            guard let audioConverter = AVAudioConverter(from: inputFormat, to: outputFormat) else {
                fatalError("Failed to create audio converter.")
            }

            let bufferDuration = audioSession.ioBufferDuration
            let sampleRate = audioSession.sampleRate
            let framesPerBuffer = Int(bufferDuration * sampleRate)

            // Install the tap on the input node
            inputNode.installTap(onBus: 0, bufferSize: AVAudioFrameCount(framesPerBuffer), format: inputFormat) { [weak self] buffer, _ in
                guard let resampledBuffer = self?.resampleBuffer(buffer: buffer, audioConverter: audioConverter) else {
                    fatalError("Failed to resample buffer.")
                }
                self?.processAudioBuffer(buffer: resampledBuffer)
            }

            // Prepare and start the audio engine
            audioEngine.prepare()
            try audioEngine.start()
            NSLog("iOS native audio recording started.")
            
            isRecording = true
        } catch {
            NSLog("Failed to start audio recording: \(error)")
        }
    }

    @objc public func stop() {
        guard isRecording else {
            NSLog("Not currently recording.")
            return
        }

        guard let audioEngine = self.audioEngine else {
            NSLog("Audio engine is nil.")
            return
        }
        
        audioEngine.inputNode.removeTap(onBus: 0)
        audioEngine.stop()
        isRecording = false
        
        // Properly clear circular buffer: reset position and fill with zeros
        writePosition = 0
        circularAudioBuffer = [Float](repeating: 0, count: circularAudioBuffer.count)
        
        NSLog("Audio recording stopped.")
    }

    @objc public func getData(offsetSamples: Int, sampleCount: Int) -> [Float] {
        return recordingQueue.sync {
            var latestData = [Float](repeating: 0, count: sampleCount)
            let bufferSize = circularAudioBuffer.count
            let start = (writePosition - sampleCount + bufferSize) % bufferSize
            if start + sampleCount <= bufferSize {
                latestData = Array(circularAudioBuffer[start..<start + sampleCount])
            } else {
                let firstPartLength = bufferSize - start
                latestData[0..<firstPartLength] = circularAudioBuffer[start..<bufferSize]
                latestData[firstPartLength..<sampleCount] = circularAudioBuffer[0..<(sampleCount - firstPartLength)]
            }
            return latestData
        }
    }

    @objc public func isMicRecording() -> Bool {
        return isRecording
    }

    @objc public func getPosition() -> Int {
        return writePosition
    }

    @objc public func getBufferLength() -> Int {
        return circularAudioBuffer.count
    }

    private func configureAudioSession(enableAEC: Bool) throws {
        NSLog("Configuring audio session with AEC: \(enableAEC)")
        if enableAEC {
            try audioSession.setCategory(.playAndRecord, mode: .videoChat, options: [.allowBluetooth, .allowBluetoothA2DP])
        } else {
            try audioSession.setCategory(.playAndRecord, mode: .default, options: [.allowBluetooth, .allowBluetoothA2DP])
        }
        try audioSession.setActive(true)
    }

    private func resampleBuffer(buffer: AVAudioPCMBuffer, audioConverter: AVAudioConverter) -> AVAudioPCMBuffer? {
        let outputFormat = audioConverter.outputFormat
        let inputSampleRate = buffer.format.sampleRate
        let outputSampleRate = outputFormat.sampleRate
        let inputFrameLength = buffer.frameLength

        // Calculate the output frame capacity to avoid data loss
        let outputFrameCapacity = AVAudioFrameCount(Double(inputFrameLength) * (outputSampleRate / inputSampleRate))

        guard let outputBuffer = AVAudioPCMBuffer(pcmFormat: outputFormat, frameCapacity: AVAudioFrameCount(outputFrameCapacity)) else {
            NSLog("Failed to create output buffer.")
            return nil
        }

        var error: NSError?
        let inputBlock: AVAudioConverterInputBlock = { inNumPackets, outStatus in
            outStatus.pointee = .haveData
            return buffer
        }

        audioConverter.convert(to: outputBuffer, error: &error, withInputFrom: inputBlock)

        if let error = error {
            NSLog("Error during audio conversion: \(error.localizedDescription)")
            return nil
        }

        return outputBuffer
    }

    private func processAudioBuffer(buffer: AVAudioPCMBuffer) {
        guard let channelData = buffer.floatChannelData else { return }
        let channelDataPointer = channelData[0]
        let frameLength = Int(buffer.frameLength)
        
        recordingQueue.sync {
            if writePosition + frameLength <= circularAudioBuffer.count {
                circularAudioBuffer.replaceSubrange(writePosition..<writePosition + frameLength, with: Array(UnsafeBufferPointer(start: channelDataPointer, count: frameLength)))
                writePosition += frameLength
                writePosition %= circularAudioBuffer.count
            } else {
                let firstPartLength = circularAudioBuffer.count - writePosition
                circularAudioBuffer.replaceSubrange(writePosition..<circularAudioBuffer.count, with: Array(UnsafeBufferPointer(start: channelDataPointer, count: firstPartLength)))
                let secondPartLength = frameLength - firstPartLength
                circularAudioBuffer.replaceSubrange(0..<secondPartLength, with: Array(UnsafeBufferPointer(start: channelDataPointer + firstPartLength, count: secondPartLength)))
                writePosition = secondPartLength
            }
        }
    }

    @objc public func registerAudioRouteChangeListener() {
        NotificationCenter.default.addObserver(
            forName: AVAudioSession.routeChangeNotification,
            object: nil,
            queue: .main
        ) { notification in
            // Get the current route
            let audioSession = AVAudioSession.sharedInstance()
            let currentRoute = audioSession.currentRoute.outputs.first?.portName ?? "Unknown"

            // Compare the current route with the previous route
            if self.previousRoute != currentRoute {
                // Route has changed
                self.previousRoute = currentRoute // Update the previous route

                // Notify Unity about the route change. "Tests" is the name of the Unity GameObject
                // containing the script AudioRecordingHandler
                // UnitySendMessage("Tests", "OnAudioRouteChanged", "")
            }
        }
    }

    @objc public func unregisterAudioRouteChangeListener() {
        NotificationCenter.default.removeObserver(
            self,
            name: AVAudioSession.routeChangeNotification,
            object: nil
        )
    }

    @objc public func checkAudioSessionConfiguration() {
        let audioSession = AVAudioSession.sharedInstance()
        NSLog("Current audio session configuration:")
        NSLog("Category: \(audioSession.category)")
        NSLog("Mode: \(audioSession.mode)")
        NSLog("Options: \(audioSession.categoryOptions)")
        NSLog("Sample Rate: \(audioSession.sampleRate)")
        NSLog("IO Buffer Duration: \(audioSession.ioBufferDuration)")
    }

    @objc public func forceAECConfiguration() {
        do {
            let audioSession = AVAudioSession.sharedInstance()
            try audioSession.setCategory(.playAndRecord, mode: .videoChat, options: [.allowBluetooth, .allowBluetoothA2DP])
            try audioSession.setActive(true)
            NSLog("Forced AEC configuration applied")
        } catch {
            NSLog("Failed to force AEC configuration: \(error)")
        }
    }
}
