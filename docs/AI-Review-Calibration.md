# AI review calibration

## Recommended settings

**Marqo:** 69% suggestion threshold. **Gemma:** More suggestions with Thorough (1024 px). Choose **Use recommended settings** in AI draft check, then save or run. Saved Marqo thresholds and image sizes remain available; the button applies the recommendation to the selected model.

More suggestions uses a broader rubric for nudity, suggestive poses and sexualized artwork. Fewer suggestions and Nudity only retain conservative rules and separate cached scores. Custom rules, device choices and human review decisions are preserved.

## Held-out comparison

I evaluated the released models and Windows preprocessing on the [deepghs/nsfw_detect dataset](https://huggingface.co/datasets/deepghs/nsfw_detect?not-for-all-audiences=true). Neutral is the negative class; hentai, porn and sexy are positive classes. The drawings folder contains mixed content and is excluded from detection and false-positive calculations.

The comparison below uses the same 3,504 held-out images for each setting: 1,704 neutral images and 600 from each positive class. Each image represents a different exact/near-duplicate family. No image in this cohort was used to choose thresholds, prompt wording or image size.

| Setting | Positive images detected | Neutral images flagged |
| --- | ---: | ---: |
| Marqo previous (7%) | 1,763/1,800 (97.9%) | 825/1,704 (48.4%) |
| Marqo tuned (69%) | 1,501/1,800 (83.4%) | 81/1,704 (4.8%) |
| Gemma previous (256 px, More suggestions) | 1,113/1,800 (61.8%) | 19/1,704 (1.1%) |
| Gemma tuned (1024 px, More suggestions) | 1,640/1,800 (91.1%) | 63/1,704 (3.7%) |

The previous settings are from 0.3.30-preview. The Windows beta includes the tuned settings first introduced in 0.3.31-preview. Gemma improves detection with a moderate increase in neutral flags. Marqo reduces neutral flags substantially while missing more positive images. These settings optimize a tradeoff; they do not maximize sensitivity and specificity independently.

| Tuned setting | Hentai detected | Porn detected | Sexy detected |
| --- | ---: | ---: | ---: |
| Marqo tuned | 552/600 (92.0%) | 534/600 (89.0%) | 415/600 (69.2%) |
| Gemma tuned | 552/600 (92.0%) | 545/600 (90.8%) | 543/600 (90.5%) |

For tuned Marqo, the 95% Wilson interval is 81.6%–85.0% for detection and 3.8%–5.9% for neutral flags. For tuned Gemma, it is 89.7%–92.3% and 2.9%–4.7%, respectively. These intervals describe this sample and do not establish a guaranteed deployment error rate.

## Selection and image size

I checked all 28,000 source files and found no decoding failures. Exact hashing identified 191 repeated copies. Conservative visual hashing grouped related copies into 27,407 families before group-disjoint development, validation and test selection. I scored all 22,400 labeled images with Marqo; its tuning and validation calculations use one representative per labeled family.

I selected thresholds and Gemma rules to retain high positive-class detection with an observed neutral flag rate of at most 5% on validation data. The Gemma validation sample contains 900 labeled positives and 600 neutral images, plus 200 unlabeled drawings that do not influence selection. Settings were frozen before evaluating the held-out cohort.

With the selected Gemma wording, validation detection was 826/900 (91.8%) at 256 px and 837/900 (93.0%) at 1024 px. Both sizes flagged 28/600 neutral images (4.7%). The 1024 px recommendation therefore reflects a modest measured gain. The revised wording accounts for most of the improvement over the previous release. Development comparisons at 512 px did not consistently improve detection.

Larger copies preserve aspect ratio and are generated from original files. Originals are unchanged, and small images are not enlarged. Thorough requires accessible originals and can take longer, especially on CPU. Quick remains available for saved-preview review.

## Marqo sensitivity choices

These alternative thresholds were selected on validation data and frozen before the held-out evaluation. They use the same 3,504 labeled test images as the main comparison. You can set any of them with Marqo's slider.

| Threshold | Positive images detected | Neutral images flagged |
| --- | ---: | ---: |
| 33% | 1,598/1,800 (88.8%) | 177/1,704 (10.4%) |
| 69% (recommended) | 1,501/1,800 (83.4%) | 81/1,704 (4.8%) |
| 89% | 1,289/1,800 (71.6%) | 36/1,704 (2.1%) |

Lower thresholds retain more possible matches; higher thresholds reduce neutral flags and miss more positives.

## Limits

- Folder labels are benchmark labels, not independently verified ground truth. Risqué content is subjective.
- The 300 mixed drawings scored alongside the held-out cohort have no image-level binary labels, so their flag counts do not establish accuracy or a false-positive rate.
- Prior model-training overlap with this dataset is unknown. Held-out here means held out from this tuning process.
- This curated dataset does not establish expected precision, sensitivity or false-positive rates for a particular workplace collection.
- Gemma priorities are category scores, not calibrated confidence percentages. Marqo thresholds are operating choices, not guarantees of certainty.
- Confirm every suggestion yourself. Neither model proves an image safe when it produces no flag.
