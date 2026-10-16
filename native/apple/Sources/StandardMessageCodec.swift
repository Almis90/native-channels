import CoreFoundation
import Foundation

/// Retains arbitrary keys, including null and collections, without host-language key coercion.
public struct StandardMessageMap {
    public let entries: [(key: Any?, value: Any?)]
    public init(_ entries: [(key: Any?, value: Any?)]) { self.entries = entries }
}

/// Compact little-endian values and typed arrays. Select explicitly on both peers.
public struct StandardMessageCodec: MessageCodec {
    public init() {}
    public func encode(_ value: Any?) throws -> Data {
        let writer = StandardWriter()
        try writer.value(value, depth: 0)
        return writer.data
    }
    public func decode(_ data: Data) throws -> Any? {
        guard data.count <= standardMaximumBytes else { throw invalidStandard() }
        let reader = StandardReader(data)
        let value = try reader.value(depth: 0)
        guard reader.remaining == 0 else { throw invalidStandard() }
        return value
    }
}

private let standardMaximumBytes = 16 * 1024 * 1024
private func invalidStandard() -> ChannelError {
    ChannelError("serialization_error", "Malformed or unsupported standard message value")
}

private final class StandardWriter {
    var data = Data()
    private func ensure(_ count: Int) throws {
        guard count >= 0, count <= standardMaximumBytes - data.count else { throw invalidStandard() }
    }
    private func byte(_ value: UInt8) throws {
        try ensure(1)
        data.append(value)
    }
    private func raw(_ bytes: Data) throws {
        try ensure(bytes.count)
        data.append(bytes)
    }
    private func number(_ value: UInt64, width: Int) throws {
        try ensure(width)
        for index in 0..<width { data.append(UInt8(truncatingIfNeeded: value >> (index * 8))) }
    }
    private func size(_ count: Int) throws {
        guard count >= 0, count <= Int(UInt32.max) else { throw invalidStandard() }
        if count < 254 {
            try byte(UInt8(count))
        } else if count <= 65535 {
            try byte(254)
            try number(UInt64(count), width: 2)
        } else {
            try byte(255)
            try number(UInt64(count), width: 4)
        }
    }
    private func align(_ width: Int) throws {
        while data.count % width != 0 { try byte(0) }
    }
    private func array<T>(_ tag: UInt8, _ items: [T], width: Int, bits: (T) -> UInt64) throws {
        try byte(tag)
        try size(items.count)
        try align(width)
        guard items.count <= (standardMaximumBytes - data.count) / width else { throw invalidStandard() }
        for item in items { try number(bits(item), width: width) }
    }
    func value(_ value: Any?, depth: Int) throws {
        guard depth <= 64 else { throw invalidStandard() }
        guard let value, !(value is NSNull) else {
            try byte(0)
            return
        }
        // Check exact Swift types before Foundation bridging can coerce Bool/numbers or typed arrays.
        let type = Swift.type(of: value)
        // Foundation containers may contain reference cycles. Require native Swift containers,
        // and reject decimal numbers before NSNumber bridging can round them to Double.
        if type is NSArray.Type || type is NSDictionary.Type || type is NSDecimalNumber.Type {
            throw invalidStandard()
        }
        if type == Bool.self {
            try byte((value as! Bool) ? 1 : 2)
        } else if type == Int32.self {
            try byte(3)
            try number(UInt64(UInt32(bitPattern: value as! Int32)), width: 4)
        } else if type == Int64.self || type == Int.self {
            let integer = type == Int.self ? Int64(value as! Int) : value as! Int64
            try byte(4)
            try number(UInt64(bitPattern: integer), width: 8)
        } else if type == Double.self || type == Float.self {
            let number = type == Float.self ? Double(value as! Float) : value as! Double
            try byte(6)
            try align(8)
            try self.number(number.bitPattern, width: 8)
        } else if type == [Int32].self {
            try array(9, value as! [Int32], width: 4) { UInt64(UInt32(bitPattern: $0)) }
        } else if type == [Int64].self {
            try array(10, value as! [Int64], width: 8) { UInt64(bitPattern: $0) }
        } else if type == [Double].self {
            try array(11, value as! [Double], width: 8) { $0.bitPattern }
        } else if type == [Float].self {
            try array(14, value as! [Float], width: 4) { UInt64($0.bitPattern) }
        } else if let text = value as? String {
            let count = text.utf8.count
            try byte(7)
            try size(count)
            try ensure(count)
            try raw(Data(text.utf8))
        } else if let bytes = value as? Data {
            try byte(8)
            try size(bytes.count)
            try raw(bytes)
        } else if let map = value as? StandardMessageMap {
            try byte(13)
            try size(map.entries.count)
            for entry in map.entries {
                try self.value(entry.key, depth: depth + 1)
                try self.value(entry.value, depth: depth + 1)
            }
        } else if let map = value as? [String: Any] {
            try byte(13)
            try size(map.count)
            for (key, item) in map {
                try self.value(key, depth: depth + 1)
                try self.value(item, depth: depth + 1)
            }
        } else if let items = value as? [Any] {
            try byte(12)
            try size(items.count)
            for item in items { try self.value(item, depth: depth + 1) }
        } else if let number = value as? NSNumber {
            if CFGetTypeID(number) == CFBooleanGetTypeID() {
                try byte(number.boolValue ? 1 : 2)
            } else {
                switch String(cString: number.objCType) {
                case "c", "s", "i": try self.value(number.int32Value, depth: depth)
                case "l", "q": try self.value(number.int64Value, depth: depth)
                case "f", "d": try self.value(number.doubleValue, depth: depth)
                default: throw invalidStandard()
                }
            }
        } else {
            throw invalidStandard()
        }
    }
}

private final class StandardReader {
    private let bytes: [UInt8]
    private var position = 0
    var remaining: Int { bytes.count - position }
    init(_ data: Data) { bytes = Array(data) }
    private func need(_ count: Int) throws {
        guard count >= 0, count <= remaining else { throw invalidStandard() }
    }
    private func byte() throws -> UInt8 {
        try need(1)
        defer { position += 1 }
        return bytes[position]
    }
    private func raw(_ count: Int) throws -> Data {
        try need(count)
        defer { position += count }
        return Data(bytes[position..<(position + count)])
    }
    private func number(_ width: Int) throws -> UInt64 {
        try need(width)
        var result: UInt64 = 0
        for index in 0..<width { result |= UInt64(try byte()) << (index * 8) }
        return result
    }
    private func size() throws -> Int {
        let first = try byte()
        return try first == 254 ? Int(number(2)) : first == 255 ? Int(number(4)) : Int(first)
    }
    private func align(_ width: Int) throws {
        while position % width != 0 { guard try byte() == 0 else { throw invalidStandard() } }
    }
    private func array<T>(_ width: Int, convert: (UInt64) -> T) throws -> [T] {
        let count = try size()
        try align(width)
        guard count <= remaining / width else { throw invalidStandard() }
        var result: [T] = []
        result.reserveCapacity(count)
        for _ in 0..<count { result.append(convert(try number(width))) }
        return result
    }
    func value(depth: Int) throws -> Any? {
        guard depth <= 64 else { throw invalidStandard() }
        switch try byte() {
        case 0: return nil
        case 1: return true
        case 2: return false
        case 3: return Int32(bitPattern: UInt32(try number(4)))
        case 4: return Int64(bitPattern: try number(8))
        case 6:
            try align(8)
            return Double(bitPattern: try number(8))
        case 7:
            guard let text = String(data: try raw(size()), encoding: .utf8) else { throw invalidStandard() }
            return text
        case 8: return try raw(size())
        case 9: return try array(4) { Int32(bitPattern: UInt32($0)) }
        case 10: return try array(8) { Int64(bitPattern: $0) }
        case 11: return try array(8) { Double(bitPattern: $0) }
        case 14: return try array(4) { Float(bitPattern: UInt32($0)) }
        case 12:
            let count = try size()
            guard count <= remaining else { throw invalidStandard() }
            var list: [Any] = []
            for _ in 0..<count { list.append(try value(depth: depth + 1) ?? NSNull()) }
            return list
        case 13:
            let count = try size()
            guard count <= remaining / 2 else { throw invalidStandard() }
            var entries: [(key: Any?, value: Any?)] = []
            var named: [String: Any] = [:]
            var allNamed = true
            for _ in 0..<count {
                let key = try value(depth: depth + 1)
                let item = try value(depth: depth + 1)
                entries.append((key, item))
                if let name = key as? String, named[name] == nil {
                    named[name] = item ?? NSNull()
                } else {
                    allNamed = false
                }
            }
            return allNamed ? named : StandardMessageMap(entries)
        default: throw invalidStandard()
        }
    }
}
