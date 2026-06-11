const express = require('express');
const { S3Client, GetObjectCommand } = require('@aws-sdk/client-s3');
const { Upload } = require('@aws-sdk/lib-storage');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const ExcelJS = require('exceljs');

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 3001;
const AWS_REGION = process.env.AWS_REGION || 'us-east-1';

const s3 = new S3Client({ region: AWS_REGION });

const DEFAULT_COL_WIDTH = 8.43;
const DEFAULT_ROW_HEIGHT = 15.0;
const CHARS_TO_MM = 2.1;
const PT_TO_MM = 0.3528;
const MARGIN_MM = 15.0;

async function presizeWorkbook(inputPath, outputPath) {
    const workbook = new ExcelJS.Workbook();
    await workbook.xlsx.readFile(inputPath);
    const sheetNames = [];
    workbook.eachSheet((worksheet) => {
        sheetNames.push(worksheet.name);
        // Skip chart sheets — they have no columns/rows to page-size
        if (!worksheet.columns || typeof worksheet.columns.forEach !== 'function') {
            console.log(`[convert-xlsx] Skipping chart sheet: ${worksheet.name}`);
            return;
        }
        let totalColMm = 0;
        worksheet.columns.forEach(col => {
            if (col && col.width !== undefined) {
                totalColMm += (col.width || DEFAULT_COL_WIDTH) * CHARS_TO_MM;
            }
        });
        let totalRowMm = 0;
        worksheet.eachRow((row) => {
            totalRowMm += (row.height || DEFAULT_ROW_HEIGHT) * PT_TO_MM;
        });
        // ADO#5113: use A4 minimum to avoid LibreOffice producing blank output from zero-column sheets
        const widthMm = Math.max(totalColMm + MARGIN_MM * 2, 210);
        const heightMm = Math.max(totalRowMm + MARGIN_MM * 2, 297);
        worksheet.pageSetup.paperWidth = `${Math.round(widthMm)}mm`;
        worksheet.pageSetup.paperHeight = `${Math.round(heightMm)}mm`;
        worksheet.pageSetup.fitToPage = true;
        worksheet.pageSetup.fitToWidth = 1;
        worksheet.pageSetup.fitToHeight = 1;
        // Do NOT set orientation — LibreOffice reads paperWidth/paperHeight literally
    });
    await workbook.xlsx.writeFile(outputPath);
    return sheetNames;
}

app.get('/health', (req, res) => {
    res.json({ status: 'ok' });
});

app.post('/convert', async (req, res) => {
    const { artifactId, s3Key, userId, outputBucket } = req.body || {};
    if (!artifactId || !s3Key || !userId || !outputBucket) {
        return res.status(400).json({ error: 'Missing required fields: artifactId, s3Key, userId, outputBucket' });
    }

    const pptxPath = `/tmp/${artifactId}.pptx`;
    const pdfPath = `/tmp/${artifactId}.pdf`;

    try {
        console.log(`[convert] Downloading s3://${outputBucket}/${s3Key} → ${pptxPath} (artifactId=${artifactId})`);
        const getCmd = new GetObjectCommand({ Bucket: outputBucket, Key: s3Key });
        const s3Resp = await s3.send(getCmd);
        await new Promise((resolve, reject) => {
            const writeStream = fs.createWriteStream(pptxPath);
            s3Resp.Body.pipe(writeStream);
            writeStream.on('finish', resolve);
            writeStream.on('error', reject);
            s3Resp.Body.on('error', reject);
        });

        const loProfileDir = `/tmp/lo-profile-${artifactId}`;
        fs.mkdirSync(loProfileDir, { recursive: true });
        console.log(`[convert] Running LibreOffice for artifactId=${artifactId}`);
        await new Promise((resolve, reject) => {
            const lo = spawn('libreoffice', [
                '--headless',
                '--convert-to', 'pdf',
                '--outdir', '/tmp',
                pptxPath
            ], { env: { ...process.env, HOME: loProfileDir } });

            let stderrBuf = '';
            lo.stdout.on('data', (data) => {
                data.toString().split('\n').filter(Boolean).forEach(line =>
                    console.log(`[lo-stdout] ${line}`)
                );
            });
            lo.stderr.on('data', (data) => {
                const text = data.toString();
                stderrBuf += text;
                text.split('\n').filter(Boolean).forEach(line =>
                    console.log(`[lo-stderr] ${line}`)
                );
            });

            const timer = setTimeout(() => {
                lo.kill();
                reject(new Error('timeout'));
            }, 5 * 60 * 1000);

            lo.on('close', (code) => {
                clearTimeout(timer);
                console.log(`[convert] LibreOffice exit code=${code} for artifactId=${artifactId}`);
                if (code === 0) {
                    resolve();
                } else {
                    reject(new Error(`LibreOffice exited with code ${code}${stderrBuf ? ': ' + stderrBuf.trim() : ''}`));
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

        const previewS3Key = `workspaces/${userId}/previews/temp/${artifactId}.pdf`;
        console.log(`[convert] Uploading PDF to s3://${outputBucket}/${previewS3Key}`);
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

        console.log(`[convert] Done: artifactId=${artifactId} key=${previewS3Key}`);
        return res.json({ status: 'done', previewS3Key });
    } catch (err) {
        if (err.isTimeout) {
            console.error(`[convert] Timeout converting artifactId=${artifactId}`);
            return res.status(504).json({ error: 'Conversion timed out' });
        }
        console.error(`[convert] Error for artifactId=${artifactId}:`, err.message);
        return res.status(500).json({ error: 'Conversion failed', detail: err.message });
    } finally {
        try { fs.unlinkSync(pptxPath); } catch (_) {}
        try { fs.unlinkSync(pdfPath); } catch (_) {}
        try { fs.rmSync(`/tmp/lo-profile-${artifactId}`, { recursive: true, force: true }); } catch (_) {}
    }
});

app.post('/convert-xlsx', async (req, res) => {
    const { artifactId, s3Key, userId, outputBucket } = req.body || {};
    if (!artifactId || !s3Key || !userId || !outputBucket) {
        return res.status(400).json({ error: 'Missing required fields: artifactId, s3Key, userId, outputBucket' });
    }

    const xlsxPath = `/tmp/${artifactId}.xlsx`;
    const pagesizedPath = `/tmp/${artifactId}-pagesized.xlsx`;
    const pagesizedPdfPath = `/tmp/${artifactId}-pagesized.pdf`;
    const pdfPath = `/tmp/${artifactId}.pdf`;

    try {
        console.log(`[convert-xlsx] Downloading s3://${outputBucket}/${s3Key} → ${xlsxPath} (artifactId=${artifactId})`);
        const getCmd = new GetObjectCommand({ Bucket: outputBucket, Key: s3Key });
        const s3Resp = await s3.send(getCmd);
        await new Promise((resolve, reject) => {
            const writeStream = fs.createWriteStream(xlsxPath);
            s3Resp.Body.pipe(writeStream);
            writeStream.on('finish', resolve);
            writeStream.on('error', reject);
            s3Resp.Body.on('error', reject);
        });

        console.log(`[convert-xlsx] Pre-sizing workbook for artifactId=${artifactId}`);
        const sheetNames = await presizeWorkbook(xlsxPath, pagesizedPath);
        console.log(`[convert-xlsx] Pre-sized ${sheetNames.length} sheets: ${sheetNames.join(', ')}`);

        const loProfileDir = `/tmp/lo-profile-${artifactId}`;
        fs.mkdirSync(loProfileDir, { recursive: true });
        console.log(`[convert-xlsx] Running LibreOffice for artifactId=${artifactId}`);
        await new Promise((resolve, reject) => {
            const lo = spawn('libreoffice', [
                '--headless',
                '--convert-to', 'pdf',
                '--outdir', '/tmp',
                pagesizedPath
            ], { env: { ...process.env, HOME: loProfileDir } });

            let stderrBuf = '';
            lo.stdout.on('data', (data) => {
                data.toString().split('\n').filter(Boolean).forEach(line =>
                    console.log(`[lo-stdout] ${line}`)
                );
            });
            lo.stderr.on('data', (data) => {
                const text = data.toString();
                stderrBuf += text;
                text.split('\n').filter(Boolean).forEach(line =>
                    console.log(`[lo-stderr] ${line}`)
                );
            });

            const timer = setTimeout(() => {
                lo.kill();
                reject(new Error('timeout'));
            }, 5 * 60 * 1000);

            lo.on('close', (code) => {
                clearTimeout(timer);
                console.log(`[convert-xlsx] LibreOffice exit code=${code} for artifactId=${artifactId}`);
                if (code === 0) {
                    resolve();
                } else {
                    reject(new Error(`LibreOffice exited with code ${code}${stderrBuf ? ': ' + stderrBuf.trim() : ''}`));
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

        fs.renameSync(pagesizedPdfPath, pdfPath);

        const previewS3Key = `workspaces/${userId}/previews/temp/${artifactId}.pdf`;
        console.log(`[convert-xlsx] Uploading PDF to s3://${outputBucket}/${previewS3Key}`);
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

        console.log(`[convert-xlsx] Done: artifactId=${artifactId} key=${previewS3Key} sheets=${sheetNames.join(',')}`);
        return res.json({ status: 'done', previewS3Key, sheetNames });
    } catch (err) {
        if (err.isTimeout) {
            console.error(`[convert-xlsx] Timeout converting artifactId=${artifactId}`);
            return res.status(504).json({ error: 'Conversion timed out' });
        }
        console.error(`[convert-xlsx] Error for artifactId=${artifactId}:`, err.message);
        return res.status(500).json({ error: 'Conversion failed', detail: err.message });
    } finally {
        try { fs.unlinkSync(xlsxPath); } catch (_) {}
        try { fs.unlinkSync(pagesizedPath); } catch (_) {}
        try { fs.unlinkSync(pagesizedPdfPath); } catch (_) {}
        try { fs.unlinkSync(pdfPath); } catch (_) {}
        try { fs.rmSync(`/tmp/lo-profile-${artifactId}`, { recursive: true, force: true }); } catch (_) {}
    }
});

app.listen(PORT, () => {
    console.log(`[converter] Listening on port ${PORT}`);
});
