const express = require('express');
const { S3Client, GetObjectCommand } = require('@aws-sdk/client-s3');
const { Upload } = require('@aws-sdk/lib-storage');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 3001;
const CONVERTER_API_KEY = process.env.CONVERTER_API_KEY || '';
const AWS_REGION = process.env.AWS_REGION || 'us-east-1';

const s3 = new S3Client({ region: AWS_REGION });

function authMiddleware(req, res, next) {
    if (!CONVERTER_API_KEY) return next(); // dev mode — skip auth
    const authHeader = req.headers['authorization'] || '';
    if (authHeader !== `Bearer ${CONVERTER_API_KEY}`) {
        return res.status(401).json({ error: 'Unauthorized' });
    }
    next();
}

app.get('/health', (req, res) => {
    res.json({ status: 'ok' });
});

app.post('/convert', authMiddleware, async (req, res) => {
    const { artifactId, s3Key, userId, outputBucket } = req.body || {};
    if (!artifactId || !s3Key || !userId || !outputBucket) {
        return res.status(400).json({ error: 'Missing required fields: artifactId, s3Key, userId, outputBucket' });
    }

    const pptxPath = `/tmp/${artifactId}.pptx`;
    const pdfPath = `/tmp/${artifactId}.pdf`;

    try {
        // Step 1: Download PPTX from S3
        console.log(`[converter] Downloading s3://${outputBucket}/${s3Key} → ${pptxPath}`);
        const getCmd = new GetObjectCommand({ Bucket: outputBucket, Key: s3Key });
        const s3Resp = await s3.send(getCmd);
        await new Promise((resolve, reject) => {
            const writeStream = fs.createWriteStream(pptxPath);
            s3Resp.Body.pipe(writeStream);
            writeStream.on('finish', resolve);
            writeStream.on('error', reject);
            s3Resp.Body.on('error', reject);
        });

        // Step 2: Convert with LibreOffice
        console.log(`[converter] Running LibreOffice conversion for ${artifactId}`);
        await new Promise((resolve, reject) => {
            const lo = spawn('libreoffice', [
                '--headless',
                `--env:UserInstallation=file:///tmp/lo-profile-${artifactId}`,
                '--convert-to', 'pdf',
                '--outdir', '/tmp',
                pptxPath
            ]);

            const timer = setTimeout(() => {
                lo.kill();
                reject(new Error('timeout'));
            }, 5 * 60 * 1000);

            lo.on('close', (code) => {
                clearTimeout(timer);
                if (code === 0) {
                    resolve();
                } else {
                    reject(new Error(`LibreOffice exited with code ${code}`));
                }
            });

            lo.on('error', (err) => {
                clearTimeout(timer);
                reject(err);
            });
        }).catch(err => {
            if (err.message === 'timeout') {
                const timeoutErr = new Error('Conversion timed out');
                timeoutErr.isTimeout = true;
                throw timeoutErr;
            }
            throw err;
        });

        // Step 3: Upload PDF to S3
        const previewS3Key = `workspaces/${userId}/previews/temp/${artifactId}.pdf`;
        console.log(`[converter] Uploading PDF to s3://${outputBucket}/${previewS3Key}`);
        const fileStream = fs.createReadStream(pdfPath);
        const upload = new Upload({
            client: s3,
            params: {
                Bucket: outputBucket,
                Key: previewS3Key,
                Body: fileStream,
                ContentType: 'application/pdf'
            }
        });
        await upload.done();

        console.log(`[converter] Done: ${artifactId}`);
        return res.json({ status: 'done', previewS3Key });
    } catch (err) {
        if (err.isTimeout) {
            console.error(`[converter] Timeout converting ${artifactId}`);
            return res.status(504).json({ error: 'Conversion timed out' });
        }
        console.error(`[converter] Error converting ${artifactId}:`, err.message);
        return res.status(500).json({ error: 'Conversion failed', detail: err.message });
    } finally {
        // Cleanup temp files
        try { fs.unlinkSync(pptxPath); } catch (_) {}
        try { fs.unlinkSync(pdfPath); } catch (_) {}
        try { fs.rmSync(`/tmp/lo-profile-${artifactId}`, { recursive: true, force: true }); } catch (_) {}
    }
});

app.listen(PORT, () => {
    console.log(`[converter] Listening on port ${PORT}`);
});
