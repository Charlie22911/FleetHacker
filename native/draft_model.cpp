#include <opencv2/core.hpp>
#include <opencv2/dnn.hpp>
#include <opencv2/imgproc.hpp>
#include <cmath>
#include <cstdio>
#include <stdexcept>

extern "C" void* mcr_draft_open(const char* model, size_t length, char* error, size_t error_length) noexcept {
    try {
        cv::setNumThreads(2);
        auto net = cv::dnn::readNetFromONNX(model, length);
        if (net.empty()) throw std::runtime_error("The model graph is empty.");
        net.setPreferableBackend(cv::dnn::DNN_BACKEND_OPENCV);
        return new cv::dnn::Net(std::move(net));
    } catch (const std::exception& e) {
        std::snprintf(error, error_length, "%s", e.what());
        return nullptr;
    }
}

extern "C" int mcr_draft_score(void* model, unsigned char* pixels, int width, int height,
                               double* score, char* error, size_t error_length) noexcept {
    try {
        if (!model || !pixels || width < 1 || height < 1 || width > 256 || height > 256)
            throw std::runtime_error("Invalid saved preview dimensions.");
        cv::Mat image(height, width, CV_8UC3, pixels);
        int side = std::min(width, height);
        cv::Mat crop = image(cv::Rect((width - side) / 2, (height - side) / 2, side, side));
        cv::Mat resized;
        cv::resize(crop, resized, cv::Size(384, 384), 0, 0, cv::INTER_CUBIC);
        auto input = cv::dnn::blobFromImage(resized, 1.0 / 127.5, cv::Size(),
                                            cv::Scalar(127.5, 127.5, 127.5), true);
        auto& net = *static_cast<cv::dnn::Net*>(model);
        net.setInput(input, "input");
        cv::Mat result = net.forward("logits");
        if (result.total() != 2 || result.type() != CV_32F)
            throw std::runtime_error("Unexpected Marqo output shape or data type.");
        const float* logits = result.ptr<float>();
        if (!std::isfinite(logits[0]) || !std::isfinite(logits[1]))
            throw std::runtime_error("Invalid Marqo output.");
        *score = 1.0 / (1.0 + std::exp(double(logits[1]) - logits[0]));
        return 0;
    } catch (const std::exception& e) {
        std::snprintf(error, error_length, "%s", e.what());
        return 1;
    }
}

extern "C" void mcr_draft_close(void* model) noexcept {
    delete static_cast<cv::dnn::Net*>(model);
}
