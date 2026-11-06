"""Developer-only export. Pass the original Marqo safetensors file and an output path."""
import hashlib
import sys
from pathlib import Path

import numpy as np
import onnx
import onnxruntime
import timm
import torch
from safetensors.torch import load_file


def main(source, output):
    original_sha256 = "6bf2e0f64a1d20169736c2836e3a787b12379fdc08ba87f7d94a7a3d58eeefce"
    if hashlib.sha256(source.read_bytes()).hexdigest() != original_sha256:
        raise ValueError("The input is not the pinned original Marqo checkpoint.")

    torch.set_num_threads(1)
    timm.layers.set_fused_attn(False)
    model = timm.create_model("vit_tiny_patch16_384", pretrained=False, num_classes=2)
    model.load_state_dict(load_file(source))
    model.eval()

    inputs = [torch.zeros((1, 3, 384, 384)), torch.rand((1, 3, 384, 384),
              generator=torch.Generator().manual_seed(42)) * 2 - 1]
    with torch.inference_mode():
        torch.onnx.export(model, inputs[0], str(output), input_names=["input"],
                          output_names=["logits"], opset_version=17, dynamo=False)
        onnx.checker.check_model(output)
        session = onnxruntime.InferenceSession(str(output), providers=["CPUExecutionProvider"])
        for image in inputs:
            expected = model(image).numpy()
            actual = session.run(["logits"], {"input": image.numpy()})[0]
            np.testing.assert_allclose(actual, expected, rtol=1e-4, atol=1e-4)
    print(hashlib.sha256(output.read_bytes()).hexdigest(), output)


if __name__ == "__main__":
    main(Path(sys.argv[1]), Path(sys.argv[2]))
