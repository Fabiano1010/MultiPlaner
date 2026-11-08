export async function toWebp(bytes, maxWidth, maxHeight, quality, crop = false) {
    const bitmap = await createImageBitmap(new Blob([bytes]), {imageOrientation: "from-image"});
    try {

        let sx = 0, sy = 0, sw = bitmap.width, sh = bitmap.height;
        if (crop) {
            const aspect = maxWidth / maxHeight;
            if (sw / sh > aspect) {
                sw = Math.round(sh * aspect);
                sx = Math.round((bitmap.width - sw) / 2);
            } else {
                sh = Math.round(sw / aspect);
                sy = Math.round((bitmap.height - sh) / 2);
            }
        }


        const scale = Math.min(1, maxWidth / sw, maxHeight / sh);
        const w = Math.max(1, Math.round(sw * scale));
        const h = Math.max(1, Math.round(sh * scale));

        const canvas = document.createElement("canvas");
        canvas.width = w;
        canvas.height = h;
        const ctx = canvas.getContext("2d");
        ctx.imageSmoothingQuality = "high";
        ctx.drawImage(bitmap, sx, sy, sw, sh, 0, 0, w, h);

        const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/webp", quality));
        if (!blob || blob.type !== "image/webp")
            throw new Error("This browser cannot convert images to WebP.");

        return new Uint8Array(await blob.arrayBuffer());
    } finally {
        bitmap.close();
    }
}