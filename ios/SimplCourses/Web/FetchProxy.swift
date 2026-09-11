import Foundation
import WebKit

/// Performs the smart-panel providers' HTTPS requests on the page's behalf and streams the body back
/// chunk by chunk (URLSession delivers data as it arrives, so server-sent events stream just as they
/// do from the extension's background script). The page's own origin rules never apply.
final class FetchProxy: NSObject, URLSessionDataDelegate {
    private struct Job {
        let id: Int
        weak var webView: WKWebView?
        let world: WKContentWorld
    }

    private var jobsByTask: [Int: Job] = [:] // URLSessionTask.taskIdentifier → job
    private var tasksById: [Int: URLSessionDataTask] = [:] // request id → task
    private let lock = NSLock()
    private lazy var session: URLSession = {
        let configuration = URLSessionConfiguration.default
        configuration.timeoutIntervalForRequest = 120
        configuration.timeoutIntervalForResource = 600
        return URLSession(configuration: configuration, delegate: self, delegateQueue: nil)
    }()

    func start(id: Int, url: URL, method: String, headers: [String: String], body: String?, webView: WKWebView, world: WKContentWorld) {
        var request = URLRequest(url: url)
        request.httpMethod = method
        for (name, value) in headers { request.setValue(value, forHTTPHeaderField: name) }
        if let body = body { request.httpBody = body.data(using: .utf8) }
        let task = session.dataTask(with: request)
        lock.lock()
        jobsByTask[task.taskIdentifier] = Job(id: id, webView: webView, world: world)
        tasksById[id] = task
        lock.unlock()
        task.resume()
    }

    func abort(id: Int) {
        lock.lock()
        let task = tasksById[id]
        lock.unlock()
        task?.cancel()
    }

    private func job(for task: URLSessionTask) -> Job? {
        lock.lock()
        defer { lock.unlock() }
        return jobsByTask[task.taskIdentifier]
    }

    private func finish(_ task: URLSessionTask) {
        lock.lock()
        if let job = jobsByTask.removeValue(forKey: task.taskIdentifier) { tasksById[job.id] = nil }
        lock.unlock()
    }

    private func send(_ job: Job, _ js: String) {
        DispatchQueue.main.async {
            job.webView?.evaluateJavaScript(js, in: nil, in: job.world) { _ in }
        }
    }

    // MARK: - URLSessionDataDelegate

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        if let job = job(for: dataTask) {
            let http = response as? HTTPURLResponse
            var headers: [String: String] = [:]
            for (name, value) in http?.allHeaderFields ?? [:] {
                if let name = name as? String, let value = value as? String { headers[name] = value }
            }
            let status = http?.statusCode ?? 200
            if let js = JS.call("BCVBridge.fetchHead", job.id, status, HTTPURLResponse.localizedString(forStatusCode: status), headers) {
                send(job, js)
            }
        }
        completionHandler(.allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard let job = job(for: dataTask) else { return }
        if let js = JS.call("BCVBridge.fetchChunk", job.id, data.base64EncodedString()) { send(job, js) }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard let job = job(for: task) else { return }
        finish(task)
        let outcome: Any
        if let error = error {
            outcome = (error as NSError).code == NSURLErrorCancelled ? "The operation was aborted." : error.localizedDescription
        } else {
            outcome = NSNull()
        }
        if let js = JS.call("BCVBridge.fetchDone", job.id, outcome) { send(job, js) }
    }
}
